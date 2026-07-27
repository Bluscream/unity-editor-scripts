using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static Bluscream.TransformExtensions;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Prunes and optimizes VRCPhysBone components to meet target PlatformProfile limits
    /// </summary>
    public static class AvatarPhysBonePruner
    {
        public static int PrunePhysBones(GameObject avatarRoot, PlatformProfile profile, Action<string> progressCallback = null)
            => PrunePhysBones(avatarRoot, profile, PhysBonePruningStrategy.DeepestFirst, progressCallback);

        public static int PrunePhysBones(GameObject avatarRoot, PlatformProfile profile, PhysBonePruningStrategy strategy, Action<string> progressCallback = null)
        {
            if (avatarRoot == null || profile == null || strategy == PhysBonePruningStrategy.Disabled) return 0;

            List<Component> pbList = new List<Component>();
            List<Component> pbColliders = new List<Component>();
            CollectPhysBoneComponents(avatarRoot, pbList, pbColliders);

            Debug.Log($"[AvatarPhysBonePruner] Found {pbList.Count} PhysBone component(s) and {pbColliders.Count} PhysBone Collider(s) on '{avatarRoot.name}'.");

            int removedCount = 0;

            // Removal priority: deepest bones in the hierarchy first (usually accessory/detail bones)
            Comparison<Component> priority = (a, b) => b.transform.GetHierarchyDepth().CompareTo(a.transform.GetHierarchyDepth());

            // Pass 1: Remove excess PhysBone components if over component budget
            int targetComponents = profile.MaxPhysBoneComponents;
            if (pbList.Count > targetComponents)
            {
                int toRemove = pbList.Count - targetComponents;
                Debug.Log($"[AvatarPhysBonePruner] [Pass 1] PhysBones {pbList.Count} > limit {targetComponents}. Pruning {toRemove} component(s) ({strategy}).");
                progressCallback?.Invoke($"PhysBones count ({pbList.Count}) exceeds profile limit ({targetComponents}). Pruning {toRemove} components...");

                pbList.Sort(priority);
                List<Component> selection = pbList.Take(toRemove).ToList();

                // InteractiveChecklist: let the user adjust the deepest-first suggestion
                if (strategy == PhysBonePruningStrategy.InteractiveChecklist && !Application.isBatchMode)
                {
                    List<Component> userSelection = PhysBonePruneChecklistWindow.ShowChecklist(pbList, toRemove, GetPhysBoneTransformCount);
                    if (userSelection != null)
                    {
                        selection = userSelection;
                    }
                    else
                    {
                        Debug.Log("[AvatarPhysBonePruner] [Pass 1] Checklist cancelled — falling back to automatic deepest-first selection.");
                    }
                }

                foreach (Component pb in selection)
                {
                    if (pb == null) continue;
                    Debug.Log($"[AvatarPhysBonePruner] [Pass 1] Removing PhysBone on '{pb.gameObject.name}'");
                    pbList.Remove(pb);
                    Undo.DestroyObjectImmediate(pb);
                    removedCount++;
                }
            }
            else
            {
                Debug.Log($"[AvatarPhysBonePruner] [Pass 1] PhysBone component count OK: {pbList.Count} / {targetComponents} limit.");
            }

            // Pass 2: Enforce total affected transform budget by pruning additional components
            int maxTransforms = profile.MaxPhysBoneTransforms;
            if (maxTransforms < int.MaxValue)
            {
                int totalTransforms = pbList.Sum(pb => pb != null ? GetPhysBoneTransformCount(pb) : 0);
                if (totalTransforms > maxTransforms)
                {
                    Debug.Log($"[AvatarPhysBonePruner] [Pass 2] PhysBone transforms {totalTransforms} > limit {maxTransforms}. Pruning components (deepest first) until within budget.");
                    progressCallback?.Invoke($"PhysBone transform count ({totalTransforms}) exceeds limit ({maxTransforms}). Pruning...");

                    pbList.Sort(priority);
                    while (totalTransforms > maxTransforms && pbList.Count > 0)
                    {
                        Component pb = pbList[0];
                        pbList.RemoveAt(0);
                        if (pb == null) continue;
                        int t = GetPhysBoneTransformCount(pb);
                        Debug.Log($"[AvatarPhysBonePruner] [Pass 2] Removing PhysBone on '{pb.gameObject.name}' ({t} transforms)");
                        Undo.DestroyObjectImmediate(pb);
                        totalTransforms -= t;
                        removedCount++;
                    }
                }
                else
                {
                    Debug.Log($"[AvatarPhysBonePruner] [Pass 2] PhysBone transform count OK: {totalTransforms} / {maxTransforms} limit.");
                }
            }

            // Pass 3: Collider budget. Remove collider components that are no longer referenced by any
            // surviving PhysBone first (free win after Pass 1/2), then trim least-referenced colliders.
            int targetColliders = profile.MaxPhysBoneColliders;
            pbColliders.RemoveAll(c => c == null);
            if (pbColliders.Count > targetColliders)
            {
                Dictionary<Component, int> refCounts = CountColliderReferences(pbList, pbColliders);

                // Unreferenced colliders go first, then ascending by reference count
                List<Component> removalOrder = pbColliders.OrderBy(c => refCounts.TryGetValue(c, out int n) ? n : 0).ToList();
                int toRemove = pbColliders.Count - targetColliders;
                Debug.Log($"[AvatarPhysBonePruner] [Pass 3] Colliders {pbColliders.Count} > limit {targetColliders}. Removing {toRemove} collider(s) (least referenced first).");

                for (int i = 0; i < toRemove && i < removalOrder.Count; i++)
                {
                    Component col = removalOrder[i];
                    if (col == null) continue;
                    Debug.Log($"[AvatarPhysBonePruner] [Pass 3] Removing collider on '{col.gameObject.name}' ({(refCounts.TryGetValue(col, out int n) ? n : 0)} reference(s))");
                    progressCallback?.Invoke($"Pruning PhysBone Collider {col.gameObject.name}...");
                    Undo.DestroyObjectImmediate(col);
                    removedCount++;
                }

                // Destroyed colliders leave null entries in surviving PhysBones' collider lists — compact them
                foreach (Component pb in pbList)
                    CompactColliderArray(pb);
            }
            else
            {
                Debug.Log($"[AvatarPhysBonePruner] [Pass 3] Colliders OK: {pbColliders.Count} / {targetColliders} limit.");
            }

            // Pass 4: Collision check budget. checks = affected transforms × colliders in the PhysBone's list;
            // a PhysBone with an empty collider list performs no collision checks.
            int maxChecks = profile.MaxPhysBoneCollisionChecks;
            int totalCollisionChecks = pbList.Sum(pb => GetCollisionCheckCount(pb));
            Debug.Log($"[AvatarPhysBonePruner] [Pass 4] Collision checks: {totalCollisionChecks} / {maxChecks} limit across {pbList.Count} remaining PBs.");

            if (totalCollisionChecks > maxChecks)
            {
                progressCallback?.Invoke($"Total PhysBone Collision Checks ({totalCollisionChecks}) exceeds limit ({maxChecks}). Trimming collider lists...");
                var sortedPBs = pbList.Where(pb => pb != null).OrderByDescending(GetCollisionCheckCount).ToList();
                foreach (Component pb in sortedPBs)
                {
                    int before = GetCollisionCheckCount(pb);
                    if (before == 0) continue;

                    try
                    {
                        SerializedObject so = new SerializedObject(pb);
                        SerializedProperty collidersProp = so.FindProperty("colliders");
                        if (collidersProp != null && collidersProp.isArray && collidersProp.arraySize > 0)
                        {
                            Undo.RecordObject(pb, "Clear PhysBone Colliders");
                            collidersProp.ClearArray();
                            so.ApplyModifiedProperties();

                            totalCollisionChecks -= before; // empty list = 0 checks
                            Debug.Log($"[AvatarPhysBonePruner] [Pass 4] Cleared collider list on '{pb.gameObject.name}': saved {before} checks. Running total: {totalCollisionChecks}");

                            if (totalCollisionChecks <= maxChecks) break;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[AvatarPhysBonePruner] Could not clear colliders on {pb.name}: {e.Message}");
                    }
                }

                Debug.Log($"[AvatarPhysBonePruner] [Pass 4] After collider list trimming: collision checks = {totalCollisionChecks} / {maxChecks}");
            }

            Debug.Log($"[AvatarPhysBonePruner] Pruned {removedCount} PhysBone components/colliders to meet target rank '{profile.Rank}'.");
            return removedCount;
        }

        private static void CollectPhysBoneComponents(GameObject avatarRoot, List<Component> pbList, List<Component> pbColliders)
        {
            foreach (Component c in avatarRoot.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string typeName = c.GetType().Name;
                if (typeName == "VRCPhysBone" || typeName == "VRCPhysBoneBase") pbList.Add(c);
                else if (typeName.Contains("VRCPhysBoneCollider")) pbColliders.Add(c);
            }
        }

        /// <summary>
        /// Counts how many PhysBones reference each collider component in their colliders list.
        /// </summary>
        private static Dictionary<Component, int> CountColliderReferences(List<Component> pbList, List<Component> pbColliders)
        {
            var counts = new Dictionary<Component, int>();
            foreach (Component col in pbColliders)
                if (col != null) counts[col] = 0;

            foreach (Component pb in pbList)
            {
                if (pb == null) continue;
                foreach (Component col in GetReferencedColliders(pb))
                {
                    if (col != null && counts.ContainsKey(col)) counts[col]++;
                }
            }
            return counts;
        }

        private static List<Component> GetReferencedColliders(Component pb)
        {
            var result = new List<Component>();
            try
            {
                SerializedObject so = new SerializedObject(pb);
                SerializedProperty collidersProp = so.FindProperty("colliders");
                if (collidersProp != null && collidersProp.isArray)
                {
                    for (int i = 0; i < collidersProp.arraySize; i++)
                    {
                        if (collidersProp.GetArrayElementAtIndex(i).objectReferenceValue is Component col && col != null)
                            result.Add(col);
                    }
                }
            }
            catch { /* ignore — treated as no colliders */ }
            return result;
        }

        /// <summary>
        /// Removes null/destroyed entries from a PhysBone's colliders array.
        /// </summary>
        private static void CompactColliderArray(Component pb)
        {
            if (pb == null) return;
            try
            {
                SerializedObject so = new SerializedObject(pb);
                SerializedProperty collidersProp = so.FindProperty("colliders");
                if (collidersProp == null || !collidersProp.isArray) return;

                bool modified = false;
                for (int i = collidersProp.arraySize - 1; i >= 0; i--)
                {
                    if (collidersProp.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    {
                        collidersProp.DeleteArrayElementAtIndex(i);
                        modified = true;
                    }
                }
                if (modified) so.ApplyModifiedProperties();
            }
            catch { /* non-critical */ }
        }

        /// <summary>
        /// Collision checks performed by a single PhysBone: affected transforms × colliders in its list.
        /// An empty collider list means the PhysBone performs no collision checks.
        /// </summary>
        private static int GetCollisionCheckCount(Component pb)
        {
            if (pb == null) return 0;
            try
            {
                int colliders = GetReferencedColliders(pb).Count;
                if (colliders == 0) return 0;
                return GetPhysBoneTransformCount(pb) * colliders;
            }
            catch
            {
                return 0;
            }
        }

        private static int GetPhysBoneTransformCount(Component pb)
        {
            if (pb == null) return 1;
            try
            {
                Transform root = pb.transform;
                var rootProp = pb.GetType().GetProperty("rootTransform") ?? pb.GetType().GetProperty("RootTransform");
                if (rootProp != null && rootProp.GetValue(pb) is Transform customRoot && customRoot != null)
                {
                    root = customRoot;
                }
                return root.CountDescendants();
            }
            catch
            {
                return 1;
            }
        }

    }
}
