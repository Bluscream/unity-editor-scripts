using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Strips dead entries from an avatar's VRCExpressionParameters to fit VRChat's synced parameter budget.
    ///
    /// This pass is deliberately inert unless it has to act: the parameter list is left completely untouched
    /// while the avatar fits the budget, because a parameter that looks unused to static analysis may still be
    /// driven by tooling this optimizer cannot see. It runs only when the avatar is actually over the limit,
    /// or when the user explicitly opts in.
    ///
    /// Only parameters referenced by nothing at all are removed, and removal stops the moment the avatar is
    /// back under budget. Live parameters are never deleted — going under the cap is not worth breaking a menu.
    /// </summary>
    public static class AvatarExpressionParameterCleaner
    {
        /// <summary>Fallback for VRCExpressionParameters.MAX_PARAMETER_COST when the SDK constant cannot be read.</summary>
        private const int FallbackMaxParameterCost = 256;

        /// <summary>Bit costs per VRCExpressionParameters.ValueType (Int = 0, Float = 1, Bool = 2).</summary>
        private const int CostInt = 8, CostFloat = 8, CostBool = 1;

        /// <summary>
        /// Parameters VRChat itself drives. They are never dead, regardless of what the animators reference.
        /// </summary>
        private static readonly HashSet<string> ReservedParameterNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "IsLocal", "Viseme", "Voice", "GestureLeft", "GestureRight", "GestureLeftWeight", "GestureRightWeight",
            "AngularY", "VelocityX", "VelocityY", "VelocityZ", "VelocityMagnitude", "Upright", "Grounded",
            "Seated", "AFK", "TrackingType", "VRMode", "MuteSelf", "InStation", "Earmuffs", "IsOnFriendsList",
            "AvatarVersion", "ScaleModified", "ScaleFactor", "ScaleFactorInverse", "EyeHeightAsMeters",
            "EyeHeightAsPercent", "VRCEmote", "VRCFaceBlendH", "VRCFaceBlendV", "VRCFaceBlendMouth"
        };

        /// <summary>Suffixes VRChat appends to a PhysBone's parameter prefix.</summary>
        private static readonly string[] PhysBoneParameterSuffixes =
        {
            "_IsGrabbed", "_IsPosed", "_Angle", "_Stretch", "_Squish", "_Grasped"
        };

        /// <summary>
        /// Removes dead synced expression parameters if the avatar exceeds the budget (or the user opted in).
        /// </summary>
        /// <param name="force">Run even when the avatar already fits the budget.</param>
        /// <returns>Number of parameters removed.</returns>
        public static int CleanExpressionParameters(
            GameObject avatarRoot,
            string outputDirectory,
            bool force = false,
            Action<string> progressCallback = null)
        {
            if (avatarRoot == null) return 0;

            Component descriptor = avatarRoot.GetComponentsInChildren<Component>(true)
                .FirstOrDefault(c => c != null && c.GetType().Name == "VRCAvatarDescriptor");
            if (descriptor == null) return 0;

            var descriptorSo = new SerializedObject(descriptor);
            SerializedProperty paramsProp = descriptorSo.FindProperty("expressionParameters");
            ScriptableObject paramAsset = paramsProp?.objectReferenceValue as ScriptableObject;
            if (paramAsset == null)
            {
                Debug.Log("[AvatarExpressionParameterCleaner] Avatar has no VRCExpressionParameters asset — nothing to clean.");
                return 0;
            }

            int budget = GetMaxParameterCost(paramAsset);
            int cost = CalculateTotalCost(paramAsset);

            if (cost <= budget && !force)
            {
                Debug.Log($"[AvatarExpressionParameterCleaner] Synced parameter cost {cost} / {budget} bits is within budget — leaving parameters untouched.");
                return 0;
            }

            Debug.Log(cost > budget
                ? $"[AvatarExpressionParameterCleaner] Synced parameter cost {cost} > budget {budget} bits — cleaning dead parameters."
                : $"[AvatarExpressionParameterCleaner] Cost {cost} / {budget} bits is within budget, but cleaning was explicitly requested.");

            HashSet<string> usedNames = CollectUsedParameterNames(avatarRoot);
            List<string> usedPrefixes = CollectPhysBoneParameterPrefixes(avatarRoot);

            List<ParameterEntry> entries = ReadParameters(paramAsset);
            List<ParameterEntry> dead = entries
                .Where(e => e.NetworkSynced && e.Cost > 0 && IsDead(e.Name, usedNames, usedPrefixes))
                // Most expensive first, so the fewest possible deletions get us back under budget.
                .OrderByDescending(e => e.Cost)
                .ToList();

            if (dead.Count == 0)
            {
                Debug.LogWarning($"[AvatarExpressionParameterCleaner] Cost is {cost} / {budget} bits but every synced parameter is still referenced — nothing safe to remove. Reduce synced parameters manually (Int/Float cost {CostInt} bits each, Bool costs {CostBool}).");
                return 0;
            }

            // Select the minimum set that brings the avatar back under budget, unless we were forced.
            var toRemove = new List<ParameterEntry>();
            int projected = cost;
            foreach (ParameterEntry entry in dead)
            {
                if (projected <= budget && !force) break;
                toRemove.Add(entry);
                projected -= entry.Cost;
            }

            if (toRemove.Count == 0) return 0;

            progressCallback?.Invoke($"Removing {toRemove.Count} dead expression parameter(s)...");

            // Non-destructive: never edit the user's original asset in place.
            ScriptableObject workingAsset = CloneAsset(paramAsset, outputDirectory, "asset", "VRCExpressionParameters");
            if (workingAsset == null) return 0;

            var removedNames = new HashSet<string>(toRemove.Select(e => e.Name), StringComparer.Ordinal);
            if (!RemoveParameters(workingAsset, removedNames))
                return 0;

            paramsProp.objectReferenceValue = workingAsset;
            descriptorSo.ApplyModifiedProperties();

            foreach (ParameterEntry entry in toRemove)
                Debug.Log($"[AvatarExpressionParameterCleaner] Removed unreferenced parameter '{entry.Name}' ({entry.Cost} bit(s)).");

            // Menu controls pointing at a parameter that no longer exists would show up broken in-game.
            int menusFixed = PruneMenuControls(descriptor, descriptorSo, removedNames, outputDirectory);

            int finalCost = CalculateTotalCost(workingAsset);
            Debug.Log($"[AvatarExpressionParameterCleaner] Complete: removed {toRemove.Count} parameter(s), pruned {menusFixed} menu control(s). Cost {cost} → {finalCost} / {budget} bits.");

            if (finalCost > budget)
                Debug.LogWarning($"[AvatarExpressionParameterCleaner] Still over budget at {finalCost} / {budget} bits — the remaining synced parameters are all in use and must be reduced manually.");

            return toRemove.Count;
        }

        private struct ParameterEntry
        {
            public string Name;
            public int Cost;
            public bool NetworkSynced;
        }

        private static bool IsDead(string name, HashSet<string> usedNames, List<string> usedPrefixes)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (ReservedParameterNames.Contains(name)) return false;
            if (usedNames.Contains(name)) return false;
            // A PhysBone with parameter prefix "Hair" owns "Hair_IsGrabbed", "Hair_Angle", etc.
            foreach (string prefix in usedPrefixes)
                if (name.StartsWith(prefix, StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>
        /// Every parameter name anything on the avatar could be driving: animator parameters, parameter-driver
        /// state behaviours, and contact receivers. Anything found here is treated as live.
        /// </summary>
        private static HashSet<string> CollectUsedParameterNames(GameObject avatarRoot)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);

            foreach (Animator anim in avatarRoot.GetComponentsInChildren<Animator>(true))
            {
                if (anim == null) continue;
                CollectFromController(anim.runtimeAnimatorController, used);
            }

            // Controllers referenced by the descriptor's playable layers are not always on an Animator.
            Component descriptor = avatarRoot.GetComponentsInChildren<Component>(true)
                .FirstOrDefault(c => c != null && c.GetType().Name == "VRCAvatarDescriptor");
            if (descriptor != null)
                CollectFromDescriptorLayers(descriptor, used);

            // Contact receivers write directly into parameters.
            foreach (Component c in avatarRoot.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (!c.GetType().Name.Contains("VRCContactReceiver")) continue;
                string param = ReadStringField(c, "parameter");
                if (!string.IsNullOrEmpty(param)) used.Add(param);
            }

            return used;
        }

        private static void CollectFromDescriptorLayers(Component descriptor, HashSet<string> used)
        {
            try
            {
                var so = new SerializedObject(descriptor);
                foreach (string listName in new[] { "baseAnimationLayers", "specialAnimationLayers" })
                {
                    SerializedProperty list = so.FindProperty(listName);
                    if (list == null || !list.isArray) continue;
                    for (int i = 0; i < list.arraySize; i++)
                    {
                        SerializedProperty ctrl = list.GetArrayElementAtIndex(i).FindPropertyRelative("animatorController");
                        CollectFromController(ctrl?.objectReferenceValue as RuntimeAnimatorController, used);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarExpressionParameterCleaner] Could not read descriptor playable layers: {e.Message}");
            }
        }

        private static void CollectFromController(RuntimeAnimatorController runtimeController, HashSet<string> used)
        {
            if (runtimeController == null) return;

            AnimatorController controller = runtimeController as AnimatorController;
            if (controller == null && runtimeController is AnimatorOverrideController aoc)
                controller = aoc.runtimeAnimatorController as AnimatorController;
            if (controller == null) return;

            foreach (AnimatorControllerParameter p in controller.parameters)
                if (!string.IsNullOrEmpty(p.name)) used.Add(p.name);

            // VRCAvatarParameterDriver behaviours name parameters the controller may never declare.
            foreach (AnimatorControllerLayer layer in controller.layers)
                CollectFromStateMachine(layer.stateMachine, used);
        }

        private static void CollectFromStateMachine(AnimatorStateMachine stateMachine, HashSet<string> used)
        {
            if (stateMachine == null) return;

            foreach (ChildAnimatorState child in stateMachine.states)
            {
                if (child.state == null) continue;
                foreach (StateMachineBehaviour behaviour in child.state.behaviours)
                    CollectFromParameterDriver(behaviour, used);
            }

            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
                CollectFromStateMachine(child.stateMachine, used);
        }

        private static void CollectFromParameterDriver(StateMachineBehaviour behaviour, HashSet<string> used)
        {
            if (behaviour == null) return;
            if (!behaviour.GetType().Name.Contains("VRCAvatarParameterDriver")) return;

            try
            {
                var so = new SerializedObject(behaviour);
                SerializedProperty parameters = so.FindProperty("parameters");
                if (parameters == null || !parameters.isArray) return;

                for (int i = 0; i < parameters.arraySize; i++)
                {
                    SerializedProperty element = parameters.GetArrayElementAtIndex(i);
                    foreach (string field in new[] { "name", "source" })
                    {
                        string value = element.FindPropertyRelative(field)?.stringValue;
                        if (!string.IsNullOrEmpty(value)) used.Add(value);
                    }
                }
            }
            catch { /* an unreadable driver simply contributes no names */ }
        }

        private static List<string> CollectPhysBoneParameterPrefixes(GameObject avatarRoot)
        {
            var prefixes = new List<string>();
            foreach (Component c in avatarRoot.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string typeName = c.GetType().Name;
                if (typeName != "VRCPhysBone" && typeName != "VRCPhysBoneBase") continue;

                string prefix = ReadStringField(c, "parameter");
                if (!string.IsNullOrEmpty(prefix)) prefixes.Add(prefix);
            }
            return prefixes;
        }

        private static string ReadStringField(Component component, string fieldName)
        {
            try
            {
                var so = new SerializedObject(component);
                return so.FindProperty(fieldName)?.stringValue;
            }
            catch
            {
                return null;
            }
        }

        private static List<ParameterEntry> ReadParameters(ScriptableObject paramAsset)
        {
            var result = new List<ParameterEntry>();
            try
            {
                var so = new SerializedObject(paramAsset);
                SerializedProperty parameters = so.FindProperty("parameters");
                if (parameters == null || !parameters.isArray) return result;

                for (int i = 0; i < parameters.arraySize; i++)
                {
                    SerializedProperty element = parameters.GetArrayElementAtIndex(i);
                    string name = element.FindPropertyRelative("name")?.stringValue;
                    if (string.IsNullOrEmpty(name)) continue;

                    // networkSynced predates nothing but is absent on very old assets, where everything synced.
                    SerializedProperty syncedProp = element.FindPropertyRelative("networkSynced");
                    bool synced = syncedProp == null || syncedProp.boolValue;

                    result.Add(new ParameterEntry
                    {
                        Name = name,
                        Cost = CostOf(element.FindPropertyRelative("valueType")?.enumValueIndex ?? 0),
                        NetworkSynced = synced
                    });
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarExpressionParameterCleaner] Could not read expression parameters: {e.Message}");
            }
            return result;
        }

        private static int CostOf(int valueTypeIndex)
        {
            switch (valueTypeIndex)
            {
                case 0: return CostInt;
                case 1: return CostFloat;
                case 2: return CostBool;
                default: return CostInt;
            }
        }

        /// <summary>Total synced bit cost, preferring the SDK's own calculation when it is available.</summary>
        private static int CalculateTotalCost(ScriptableObject paramAsset)
        {
            try
            {
                MethodInfo calc = paramAsset.GetType().GetMethod("CalcTotalCost", BindingFlags.Public | BindingFlags.Instance);
                if (calc != null && calc.GetParameters().Length == 0 && calc.Invoke(paramAsset, null) is int sdkCost)
                    return sdkCost;
            }
            catch { /* fall through to the local calculation */ }

            return ReadParameters(paramAsset).Where(e => e.NetworkSynced).Sum(e => e.Cost);
        }

        private static int GetMaxParameterCost(ScriptableObject paramAsset)
        {
            try
            {
                FieldInfo maxField = paramAsset.GetType().GetField("MAX_PARAMETER_COST", BindingFlags.Public | BindingFlags.Static);
                if (maxField != null && maxField.GetValue(null) is int sdkMax && sdkMax > 0)
                    return sdkMax;
            }
            catch { /* fall through to the documented default */ }

            return FallbackMaxParameterCost;
        }

        private static bool RemoveParameters(ScriptableObject paramAsset, HashSet<string> namesToRemove)
        {
            try
            {
                var so = new SerializedObject(paramAsset);
                SerializedProperty parameters = so.FindProperty("parameters");
                if (parameters == null || !parameters.isArray) return false;

                for (int i = parameters.arraySize - 1; i >= 0; i--)
                {
                    string name = parameters.GetArrayElementAtIndex(i).FindPropertyRelative("name")?.stringValue;
                    if (!string.IsNullOrEmpty(name) && namesToRemove.Contains(name))
                        parameters.DeleteArrayElementAtIndex(i);
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(paramAsset);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarExpressionParameterCleaner] Could not remove parameters: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Clones the menu tree and drops controls whose parameter was removed, so no broken entries remain.
        /// </summary>
        private static int PruneMenuControls(
            Component descriptor,
            SerializedObject descriptorSo,
            HashSet<string> removedNames,
            string outputDirectory)
        {
            SerializedProperty menuProp = descriptorSo.FindProperty("expressionsMenu");
            ScriptableObject rootMenu = menuProp?.objectReferenceValue as ScriptableObject;
            if (rootMenu == null) return 0;

            // Read-only pre-check: if nothing in the tree points at a removed parameter, clone nothing at all.
            if (!MenuTreeReferencesRemoved(rootMenu, removedNames, new HashSet<ScriptableObject>()))
                return 0;

            var clonedMenus = new Dictionary<ScriptableObject, ScriptableObject>();
            int removed = 0;

            ScriptableObject newRoot = PruneMenu(rootMenu, removedNames, outputDirectory, clonedMenus, ref removed);
            if (removed == 0) return 0;

            if (newRoot != null && newRoot != rootMenu)
            {
                menuProp.objectReferenceValue = newRoot;
                descriptorSo.ApplyModifiedProperties();
            }
            return removed;
        }

        /// <summary>Read-only walk of the menu tree looking for any control bound to a removed parameter.</summary>
        private static bool MenuTreeReferencesRemoved(ScriptableObject menu, HashSet<string> removedNames, HashSet<ScriptableObject> visited)
        {
            if (menu == null || !visited.Add(menu)) return false;

            try
            {
                var so = new SerializedObject(menu);
                SerializedProperty controls = so.FindProperty("controls");
                if (controls == null || !controls.isArray) return false;

                for (int i = 0; i < controls.arraySize; i++)
                {
                    SerializedProperty control = controls.GetArrayElementAtIndex(i);
                    if (ControlReferencesRemovedParameter(control, removedNames)) return true;

                    if (control.FindPropertyRelative("subMenu")?.objectReferenceValue is ScriptableObject sub
                        && MenuTreeReferencesRemoved(sub, removedNames, visited))
                        return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarExpressionParameterCleaner] Could not inspect menu '{menu.name}': {e.Message}");
            }
            return false;
        }

        private static ScriptableObject PruneMenu(
            ScriptableObject menu,
            HashSet<string> removedNames,
            string outputDirectory,
            Dictionary<ScriptableObject, ScriptableObject> clonedMenus,
            ref int removedCount)
        {
            if (menu == null) return null;
            if (clonedMenus.TryGetValue(menu, out ScriptableObject already)) return already;

            // Guard against a menu tree that references itself.
            clonedMenus[menu] = menu;

            ScriptableObject working = CloneAsset(menu, outputDirectory, "asset", "VRCExpressionsMenu");
            if (working == null) return menu;
            clonedMenus[menu] = working;

            try
            {
                var so = new SerializedObject(working);
                SerializedProperty controls = so.FindProperty("controls");
                if (controls == null || !controls.isArray) return working;

                for (int i = controls.arraySize - 1; i >= 0; i--)
                {
                    SerializedProperty control = controls.GetArrayElementAtIndex(i);

                    if (ControlReferencesRemovedParameter(control, removedNames))
                    {
                        string label = control.FindPropertyRelative("name")?.stringValue ?? "(unnamed)";
                        Debug.Log($"[AvatarExpressionParameterCleaner] Removed menu control '{label}' — its parameter no longer exists.");
                        controls.DeleteArrayElementAtIndex(i);
                        removedCount++;
                        continue;
                    }

                    // Recurse into submenus so nested controls are pruned too.
                    SerializedProperty subMenuProp = control.FindPropertyRelative("subMenu");
                    if (subMenuProp?.objectReferenceValue is ScriptableObject subMenu && subMenu != null)
                    {
                        ScriptableObject newSub = PruneMenu(subMenu, removedNames, outputDirectory, clonedMenus, ref removedCount);
                        if (newSub != null) subMenuProp.objectReferenceValue = newSub;
                    }
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(working);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarExpressionParameterCleaner] Could not prune menu '{menu.name}': {e.Message}");
            }

            return working;
        }

        private static bool ControlReferencesRemovedParameter(SerializedProperty control, HashSet<string> removedNames)
        {
            string main = control.FindPropertyRelative("parameter")?.FindPropertyRelative("name")?.stringValue;
            if (!string.IsNullOrEmpty(main) && removedNames.Contains(main)) return true;

            // Puppet controls drive up to four sub-parameters; losing any one breaks the control.
            SerializedProperty subParams = control.FindPropertyRelative("subParameters");
            if (subParams != null && subParams.isArray)
            {
                for (int i = 0; i < subParams.arraySize; i++)
                {
                    string sub = subParams.GetArrayElementAtIndex(i).FindPropertyRelative("name")?.stringValue;
                    if (!string.IsNullOrEmpty(sub) && removedNames.Contains(sub)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Copies an asset into the output directory so the user's original is never modified.
        /// </summary>
        private static ScriptableObject CloneAsset(ScriptableObject source, string outputDirectory, string defaultExt, string label)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogWarning($"[AvatarExpressionParameterCleaner] Cannot duplicate {label}: '{source.name}' has no asset path. Skipping to avoid mutating a runtime-only asset.");
                return null;
            }

            string dir = !string.IsNullOrEmpty(outputDirectory) && Directory.Exists(outputDirectory)
                ? outputDirectory
                : Path.GetDirectoryName(sourcePath);

            string filename = Path.GetFileNameWithoutExtension(sourcePath);
            string ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrEmpty(ext)) ext = "." + defaultExt;

            string targetName = filename.EndsWith(" (Optimized)", StringComparison.Ordinal) ? filename : filename + " (Optimized)";
            string destPath = Path.Combine(dir, targetName + ext).Replace('\\', '/');

            if (File.Exists(destPath))
                AssetDatabase.DeleteAsset(destPath);

            if (!AssetDatabase.CopyAsset(sourcePath, destPath))
            {
                Debug.LogWarning($"[AvatarExpressionParameterCleaner] Failed to copy {label} '{sourcePath}' -> '{destPath}'. Leaving the original untouched.");
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<ScriptableObject>(destPath);
        }
    }
}
