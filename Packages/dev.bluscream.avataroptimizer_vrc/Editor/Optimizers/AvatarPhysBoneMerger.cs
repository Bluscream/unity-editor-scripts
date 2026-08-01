using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using static Bluscream.TransformExtensions;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Consolidates sibling VRCPhysBone components into a single component rooted at their shared parent,
    /// using ignoreTransforms to exclude the subtrees that were not covered by the originals.
    ///
    /// This is the non-destructive counterpart to <see cref="AvatarPhysBonePruner"/>: N sibling chains with
    /// identical settings become 1 component at the cost of exactly 1 extra affected transform (the shared
    /// parent), so motion is preserved instead of being deleted. It therefore runs *before* pruning, so the
    /// pruner only has to destroy what merging could not save.
    /// </summary>
    public static class AvatarPhysBoneMerger
    {
        /// <summary>Serialized fields the merge itself rewrites — excluded from the settings-equality test.</summary>
        private static readonly HashSet<string> MergeControlledFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "rootTransform", "ignoreTransforms"
        };

        /// <summary>Unity-internal serialized fields that never describe PhysBone behaviour.</summary>
        private static readonly HashSet<string> IgnoredFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset",
            "m_GameObject", "m_Script", "m_EditorHideFlags", "m_EditorClassIdentifier", "m_Name"
        };

        /// <summary>
        /// Merges mergeable sibling PhysBone groups until the component count fits the profile limit.
        /// </summary>
        /// <returns>Number of PhysBone components eliminated.</returns>
        public static int MergePhysBones(GameObject avatarRoot, PlatformProfile profile, Action<string> progressCallback = null)
        {
            if (avatarRoot == null || profile == null) return 0;

            List<Component> physBones = CollectPhysBones(avatarRoot);
            if (physBones.Count == 0) return 0;

            int componentLimit = profile.MaxPhysBoneComponents;
            if (physBones.Count <= componentLimit)
            {
                Debug.Log($"[AvatarPhysBoneMerger] PhysBone component count already within budget ({physBones.Count} / {componentLimit}) — nothing to merge.");
                return 0;
            }

            // A PhysBone whose GameObject is animated (component toggles, parameter drives) cannot move to
            // another GameObject without breaking those curves, so it is excluded from merging entirely.
            HashSet<string> animatedPaths = CollectAnimatedPhysBonePaths(avatarRoot);

            int transformBudget = profile.MaxPhysBoneTransforms;
            int totalTransforms = physBones.Sum(GetPhysBoneTransformCount);

            List<MergeGroup> groups = BuildMergeGroups(avatarRoot, physBones, animatedPaths);
            if (groups.Count == 0)
            {
                Debug.Log($"[AvatarPhysBoneMerger] No mergeable sibling PhysBone groups found on '{avatarRoot.name}'.");
                return 0;
            }

            // Largest groups first: they buy the most component headroom for the same +1 transform.
            groups.Sort((a, b) => b.Members.Count.CompareTo(a.Members.Count));

            int currentCount = physBones.Count;
            int eliminated = 0;

            foreach (MergeGroup group in groups)
            {
                if (currentCount <= componentLimit)
                {
                    Debug.Log($"[AvatarPhysBoneMerger] Component budget reached ({currentCount} / {componentLimit}) — stopping before over-merging.");
                    break;
                }

                // Merging re-roots the chains at the shared parent, which becomes one additional affected transform.
                int transformsAfter = totalTransforms + 1;
                if (transformsAfter > transformBudget)
                {
                    Debug.Log($"[AvatarPhysBoneMerger] Skipping group on '{group.CommonParent.name}': merging would take affected transforms to {transformsAfter} > limit {transformBudget}.");
                    continue;
                }

                progressCallback?.Invoke($"Merging {group.Members.Count} PhysBones into '{group.CommonParent.name}'...");
                if (!ExecuteMerge(group)) continue;

                int saved = group.Members.Count - 1;
                currentCount -= saved;
                eliminated += saved;
                totalTransforms = transformsAfter;

                Debug.Log($"[AvatarPhysBoneMerger] Merged {group.Members.Count} PhysBones into 1 on '{group.CommonParent.name}' " +
                          $"({group.IgnoredTransforms.Count} ignored transform(s)). Components: {currentCount} / {componentLimit}, affected transforms: {totalTransforms} / {transformBudget}.");
            }

            Debug.Log($"[AvatarPhysBoneMerger] Complete: eliminated {eliminated} PhysBone component(s) without losing motion.");
            return eliminated;
        }

        private sealed class MergeGroup
        {
            public Transform CommonParent;
            public List<Component> Members = new List<Component>();
            /// <summary>Effective root of each member, in the same order as <see cref="Members"/>.</summary>
            public List<Transform> MemberRoots = new List<Transform>();
            /// <summary>Subtrees under CommonParent that the originals did not affect, plus inherited ignores.</summary>
            public List<Transform> IgnoredTransforms = new List<Transform>();
        }

        /// <summary>
        /// Groups PhysBones whose effective roots are direct children of the same parent and whose settings
        /// are identical, so that one component can stand in for all of them.
        /// </summary>
        private static List<MergeGroup> BuildMergeGroups(GameObject avatarRoot, List<Component> physBones, HashSet<string> animatedPaths)
        {
            var byParent = new Dictionary<Transform, List<Component>>();
            var rootOf = new Dictionary<Component, Transform>();

            foreach (Component pb in physBones)
            {
                if (pb == null) continue;

                string path = AnimationUtility.CalculateTransformPath(pb.transform, avatarRoot.transform);
                if (animatedPaths.Contains(path))
                {
                    Debug.Log($"[AvatarPhysBoneMerger] Skipping PhysBone on '{pb.gameObject.name}': its GameObject is animated, re-rooting would break the curves.");
                    continue;
                }

                Transform root = GetEffectiveRoot(pb);
                if (root == null || root.parent == null) continue;

                Transform parent = root.parent;
                // Re-rooting at the avatar root would force an ignore list spanning the whole avatar.
                if (parent == avatarRoot.transform) continue;

                rootOf[pb] = root;
                if (!byParent.TryGetValue(parent, out List<Component> list))
                    byParent[parent] = list = new List<Component>();
                list.Add(pb);
            }

            // A parent that is already simulated as some PhysBone's root would end up with two overlapping roots.
            var parentsWithOwnPhysBone = new HashSet<Transform>(physBones.Where(pb => pb != null).Select(GetEffectiveRoot));

            var groups = new List<MergeGroup>();
            foreach (var kvp in byParent)
            {
                Transform parent = kvp.Key;
                if (kvp.Value.Count < 2) continue;
                if (parentsWithOwnPhysBone.Contains(parent)) continue;

                // Within a parent, only PhysBones sharing identical settings can collapse into one.
                foreach (List<Component> bucket in BucketByIdenticalSettings(kvp.Value))
                {
                    if (bucket.Count < 2) continue;

                    var group = new MergeGroup { CommonParent = parent, Members = bucket };
                    group.MemberRoots = bucket.Select(pb => rootOf[pb]).ToList();

                    var covered = new HashSet<Transform>(group.MemberRoots);
                    for (int i = 0; i < parent.childCount; i++)
                    {
                        Transform child = parent.GetChild(i);
                        if (!covered.Contains(child)) group.IgnoredTransforms.Add(child);
                    }
                    // Anything the originals already excluded stays excluded.
                    foreach (Component pb in bucket)
                        foreach (Transform ignored in GetIgnoreTransforms(pb))
                            if (ignored != null && !group.IgnoredTransforms.Contains(ignored))
                                group.IgnoredTransforms.Add(ignored);

                    groups.Add(group);
                }
            }

            return groups;
        }

        private static IEnumerable<List<Component>> BucketByIdenticalSettings(List<Component> candidates)
        {
            var buckets = new List<List<Component>>();
            foreach (Component pb in candidates)
            {
                List<Component> match = buckets.FirstOrDefault(b => HasIdenticalSettings(b[0], pb));
                if (match != null) match.Add(pb);
                else buckets.Add(new List<Component> { pb });
            }
            return buckets;
        }

        /// <summary>
        /// Compares every serialized field of two PhysBones except the ones the merge rewrites. Anything the
        /// comparison cannot read is treated as a difference, so an unknown field never merges silently.
        /// </summary>
        private static bool HasIdenticalSettings(Component a, Component b)
        {
            if (a == null || b == null) return false;
            if (a.GetType() != b.GetType()) return false;

            try
            {
                var soA = new SerializedObject(a);
                var soB = new SerializedObject(b);

                SerializedProperty itA = soA.GetIterator();
                if (!itA.NextVisible(true)) return false;

                do
                {
                    string name = itA.name;
                    if (IgnoredFields.Contains(name) || MergeControlledFields.Contains(name)) continue;

                    SerializedProperty propB = soB.FindProperty(itA.propertyPath);
                    if (propB == null) return false;
                    if (!SerializedProperty.DataEquals(itA, propB)) return false;
                }
                while (itA.NextVisible(false));

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarPhysBoneMerger] Could not compare PhysBone settings on '{a.gameObject.name}' / '{b.gameObject.name}': {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Clones the group's first member onto the shared parent, re-roots it, applies the ignore list,
        /// then destroys the originals.
        /// </summary>
        private static bool ExecuteMerge(MergeGroup group)
        {
            Component template = group.Members[0];
            GameObject host = group.CommonParent.gameObject;

            if (!ComponentUtility.CopyComponent(template))
            {
                Debug.LogWarning($"[AvatarPhysBoneMerger] Could not copy PhysBone from '{template.gameObject.name}' — group skipped.");
                return false;
            }

            var before = new HashSet<Component>(host.GetComponents<Component>());
            if (!ComponentUtility.PasteComponentAsNew(host))
            {
                Debug.LogWarning($"[AvatarPhysBoneMerger] Could not paste merged PhysBone onto '{host.name}' — group skipped.");
                return false;
            }

            Component merged = host.GetComponents<Component>().FirstOrDefault(c => c != null && !before.Contains(c));
            if (merged == null)
            {
                Debug.LogWarning($"[AvatarPhysBoneMerger] Merged PhysBone did not appear on '{host.name}' — group skipped.");
                return false;
            }
            // PasteComponentAsNew already records its own undo entry — do not register a second one.
            if (!ApplyRootAndIgnores(merged, group))
            {
                Undo.DestroyObjectImmediate(merged);
                return false;
            }

            foreach (Component pb in group.Members)
                if (pb != null) Undo.DestroyObjectImmediate(pb);

            return true;
        }

        private static bool ApplyRootAndIgnores(Component merged, MergeGroup group)
        {
            try
            {
                var so = new SerializedObject(merged);

                SerializedProperty rootProp = so.FindProperty("rootTransform");
                if (rootProp == null)
                {
                    Debug.LogWarning($"[AvatarPhysBoneMerger] PhysBone on '{merged.gameObject.name}' has no 'rootTransform' field — group skipped.");
                    return false;
                }
                rootProp.objectReferenceValue = group.CommonParent;

                SerializedProperty ignoreProp = so.FindProperty("ignoreTransforms");
                if (ignoreProp == null || !ignoreProp.isArray)
                {
                    Debug.LogWarning($"[AvatarPhysBoneMerger] PhysBone on '{merged.gameObject.name}' has no 'ignoreTransforms' array — group skipped.");
                    return false;
                }

                ignoreProp.ClearArray();
                ignoreProp.arraySize = group.IgnoredTransforms.Count;
                for (int i = 0; i < group.IgnoredTransforms.Count; i++)
                    ignoreProp.GetArrayElementAtIndex(i).objectReferenceValue = group.IgnoredTransforms[i];

                so.ApplyModifiedProperties();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarPhysBoneMerger] Could not configure merged PhysBone on '{merged.gameObject.name}': {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Paths of GameObjects whose PhysBone component is driven by an animation curve.
        /// </summary>
        private static HashSet<string> CollectAnimatedPhysBonePaths(GameObject avatarRoot)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);

            foreach (Animator anim in avatarRoot.GetComponentsInChildren<Animator>(true))
            {
                if (anim == null || anim.runtimeAnimatorController == null) continue;
                AnimationClip[] clips = anim.runtimeAnimatorController.animationClips;
                if (clips == null) continue;

                foreach (AnimationClip clip in clips)
                {
                    if (clip == null) continue;
                    foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type != null && binding.type.Name.StartsWith("VRCPhysBone", StringComparison.Ordinal))
                            paths.Add(binding.path);
                    }
                }
            }

            return paths;
        }

        private static List<Component> CollectPhysBones(GameObject avatarRoot)
        {
            var result = new List<Component>();
            foreach (Component c in avatarRoot.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string typeName = c.GetType().Name;
                if (typeName == "VRCPhysBone" || typeName == "VRCPhysBoneBase") result.Add(c);
            }
            return result;
        }

        /// <summary>The transform a PhysBone actually simulates: its explicit rootTransform, else its own.</summary>
        private static Transform GetEffectiveRoot(Component pb)
        {
            if (pb == null) return null;
            try
            {
                var rootProp = pb.GetType().GetProperty("rootTransform") ?? pb.GetType().GetProperty("RootTransform");
                if (rootProp != null && rootProp.GetValue(pb) is Transform customRoot && customRoot != null)
                    return customRoot;
            }
            catch { /* fall through to the component's own transform */ }
            return pb.transform;
        }

        private static List<Transform> GetIgnoreTransforms(Component pb)
        {
            var result = new List<Transform>();
            if (pb == null) return result;
            try
            {
                var so = new SerializedObject(pb);
                SerializedProperty prop = so.FindProperty("ignoreTransforms");
                if (prop == null || !prop.isArray) return result;
                for (int i = 0; i < prop.arraySize; i++)
                    if (prop.GetArrayElementAtIndex(i).objectReferenceValue is Transform t && t != null)
                        result.Add(t);
            }
            catch { /* treated as no inherited ignores */ }
            return result;
        }

        private static int GetPhysBoneTransformCount(Component pb)
        {
            Transform root = GetEffectiveRoot(pb);
            return root == null ? 1 : root.CountDescendants();
        }
    }
}
