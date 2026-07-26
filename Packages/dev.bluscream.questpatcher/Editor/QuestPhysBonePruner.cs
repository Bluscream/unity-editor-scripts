using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRCQuestPatcher
{
    /// <summary>
    /// Prunes and optimizes VRCPhysBone components to meet target Quest Performance limits
    /// </summary>
    public static class QuestPhysBonePruner
    {
        public static int PrunePhysBones(GameObject avatarRoot, QuestPerformanceProfile profile, Action<string> progressCallback = null)
        {
            if (avatarRoot == null || profile == null) return 0;

            Component[] components = avatarRoot.GetComponentsInChildren<Component>(true);
            List<Component> pbList = new List<Component>();
            List<Component> pbColliders = new List<Component>();

            foreach (Component c in components)
            {
                if (c == null) continue;
                string typeName = c.GetType().Name;
                if (typeName == "VRCPhysBone" || typeName == "VRCPhysBoneBase") pbList.Add(c);
                else if (typeName.Contains("VRCPhysBoneCollider")) pbColliders.Add(c);
            }

            int removedCount = 0;

            // 1. Remove excess Colliders first if over budget (Quest max 16)
            int targetColliders = Math.Min(profile.MaxPhysBoneColliders, 16);
            if (pbColliders.Count > targetColliders)
            {
                for (int i = pbColliders.Count - 1; i >= targetColliders; i--)
                {
                    if (pbColliders[i] != null)
                    {
                        progressCallback?.Invoke($"Pruning PhysBone Collider {pbColliders[i].gameObject.name}...");
                        Undo.DestroyObjectImmediate(pbColliders[i]);
                        removedCount++;
                    }
                }
            }

            // 2. Remove excess PhysBone components if over budget (Quest max 8)
            int targetComponents = Math.Min(profile.MaxPhysBoneComponents, 8);
            if (pbList.Count > targetComponents)
            {
                int toRemove = pbList.Count - targetComponents;
                progressCallback?.Invoke($"PhysBones count ({pbList.Count}) exceeds Quest limit ({targetComponents}). Pruning {toRemove} components...");

                if (profile.PruningStrategy == PhysBonePruningStrategy.ShallowestFirst)
                {
                    pbList.Sort((a, b) => GetHierarchyDepth(a.transform).CompareTo(GetHierarchyDepth(b.transform)));
                }
                else // DeepestFirst (Default)
                {
                    pbList.Sort((a, b) => GetHierarchyDepth(b.transform).CompareTo(GetHierarchyDepth(a.transform)));
                }

                for (int i = 0; i < toRemove; i++)
                {
                    if (i < pbList.Count && pbList[i] != null)
                    {
                        progressCallback?.Invoke($"Pruned PhysBone component from {pbList[i].gameObject.name}");
                        Undo.DestroyObjectImmediate(pbList[i]);
                        removedCount++;
                    }
                }
            }

            // 3. Trim collider references on remaining PhysBones until total collision checks <= 64
            pbList.RemoveAll(c => c == null);
            int totalCollisionChecks = CalculateTotalCollisionChecks(pbList);
            int maxChecks = Math.Min(profile.MaxPhysBoneCollisionChecks, 64);

            if (totalCollisionChecks > maxChecks)
            {
                progressCallback?.Invoke($"Total PhysBone Collision Checks ({totalCollisionChecks}) exceeds Quest limit ({maxChecks}). Trimming colliders...");

                // Sort PhysBones by highest collision check count first
                pbList.Sort((a, b) => GetCollisionCheckCount(b).CompareTo(GetCollisionCheckCount(a)));

                foreach (Component pb in pbList)
                {
                    if (totalCollisionChecks <= maxChecks) break;

                    try
                    {
                        SerializedObject so = new SerializedObject(pb);
                        SerializedProperty collidersProp = so.FindProperty("colliders");
                        if (collidersProp != null && collidersProp.isArray && collidersProp.arraySize > 0)
                        {
                            int before = GetCollisionCheckCount(pb);
                            collidersProp.ClearArray();
                            so.ApplyModifiedProperties();
                            int after = GetCollisionCheckCount(pb);
                            totalCollisionChecks -= (before - after);
                            removedCount++;
                            progressCallback?.Invoke($"Cleared colliders from PhysBone component on {pb.gameObject.name} to reduce collision checks.");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[QuestPhysBonePruner] Could not clear colliders on {pb.name}: {e.Message}");
                    }
                }
            }

            // 4. If total collision checks still > maxChecks, prune PhysBones with largest check counts until total <= maxChecks
            pbList.RemoveAll(c => c == null);
            totalCollisionChecks = CalculateTotalCollisionChecks(pbList);
            if (totalCollisionChecks > maxChecks)
            {
                pbList.Sort((a, b) => GetCollisionCheckCount(b).CompareTo(GetCollisionCheckCount(a)));

                for (int i = 0; i < pbList.Count; i++)
                {
                    if (totalCollisionChecks <= maxChecks) break;
                    Component pb = pbList[i];
                    if (pb != null)
                    {
                        int checks = GetCollisionCheckCount(pb);
                        Undo.DestroyObjectImmediate(pb);
                        totalCollisionChecks -= checks;
                        removedCount++;
                        progressCallback?.Invoke($"Pruned PhysBone component on {pb.gameObject.name} to enforce <= {maxChecks} collision checks.");
                    }
                }
            }

            Debug.Log($"[QuestPhysBonePruner] Pruned {removedCount} PhysBone components/colliders to meet target rank '{profile.Rank}'.");
            return removedCount;
        }

        private static int CalculateTotalCollisionChecks(List<Component> pbList)
        {
            int total = 0;
            foreach (Component pb in pbList)
            {
                if (pb != null) total += GetCollisionCheckCount(pb);
            }
            return total;
        }

        private static int GetCollisionCheckCount(Component pb, int totalAvatarColliders = 14)
        {
            if (pb == null) return 0;
            try
            {
                int transforms = GetPhysBoneTransformCount(pb);
                SerializedObject so = new SerializedObject(pb);
                SerializedProperty collidersProp = so.FindProperty("colliders");
                int explicitColliders = (collidersProp != null && collidersProp.isArray) ? collidersProp.arraySize : 0;
                int effectiveColliders = explicitColliders > 0 ? explicitColliders : totalAvatarColliders;
                return transforms * effectiveColliders;
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
                return CountTransformTree(root);
            }
            catch
            {
                return 1;
            }
        }

        private static int CountTransformTree(Transform t)
        {
            if (t == null) return 0;
            int count = 1;
            for (int i = 0; i < t.childCount; i++)
            {
                count += CountTransformTree(t.GetChild(i));
            }
            return count;
        }

        private static int GetHierarchyDepth(Transform t)
        {
            int depth = 0;
            while (t != null)
            {
                depth++;
                t = t.parent;
            }
            return depth;
        }
    }
}
