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

            Debug.Log($"[QuestPhysBonePruner] Found {pbList.Count} PhysBone component(s) and {pbColliders.Count} PhysBone Collider(s) on '{avatarRoot.name}'.");

            int removedCount = 0;
            int totalCollisionChecks = 0;
            int maxChecks = 0;

            // 1. Remove excess Colliders first if over budget (Quest max 16)
            int targetColliders = Math.Min(profile.MaxPhysBoneColliders, 16);
            if (pbColliders.Count > targetColliders)
            {
                Debug.Log($"[QuestPhysBonePruner] [Pass 1] Colliders {pbColliders.Count} > limit {targetColliders}. Removing {pbColliders.Count - targetColliders} collider(s).");
                for (int i = pbColliders.Count - 1; i >= targetColliders; i--)
                {
                    if (pbColliders[i] != null)
                    {
                        Debug.Log($"[QuestPhysBonePruner] [Pass 1] Removing collider on '{pbColliders[i].gameObject.name}'");
                        progressCallback?.Invoke($"Pruning PhysBone Collider {pbColliders[i].gameObject.name}...");
                        Undo.DestroyObjectImmediate(pbColliders[i]);
                        removedCount++;
                    }
                }
            }
            else
            {
                Debug.Log($"[QuestPhysBonePruner] [Pass 1] Colliders OK: {pbColliders.Count} / {targetColliders} limit.");
            }

            // 2. Remove excess PhysBone components if over budget (Quest max 8)
            int targetComponents = Math.Min(profile.MaxPhysBoneComponents, 8);
            if (pbList.Count > targetComponents)
            {
                int toRemove = pbList.Count - targetComponents;
                Debug.Log($"[QuestPhysBonePruner] [Pass 2] PhysBones {pbList.Count} > limit {targetComponents}. Pruning {toRemove} component(s) using strategy '{profile.PruningStrategy}'.");
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
                        int depth = GetHierarchyDepth(pbList[i].transform);
                        int transforms = GetPhysBoneTransformCount(pbList[i]);
                        Debug.Log($"[QuestPhysBonePruner] [Pass 2] Removing PhysBone on '{pbList[i].gameObject.name}' (depth={depth}, transforms={transforms})");
                        progressCallback?.Invoke($"Pruned PhysBone component from {pbList[i].gameObject.name}");
                        Undo.DestroyObjectImmediate(pbList[i]);
                        removedCount++;
                    }
                }
            }
            else
            {
                Debug.Log($"[QuestPhysBonePruner] [Pass 2] PhysBone component count OK: {pbList.Count} / {targetComponents} limit.");
            }

            // 3. Trim collider references on remaining PhysBones until total collision checks <= 64
            pbList.RemoveAll(c => c == null);
            totalCollisionChecks = CalculateTotalCollisionChecks(pbList);
            maxChecks = Math.Min(profile.MaxPhysBoneCollisionChecks, 64);
            Debug.Log($"[QuestPhysBonePruner] [Pass 3] Collision checks: {totalCollisionChecks} / {maxChecks} limit across {pbList.Count} remaining PBs.");

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
                            int reduced = before - after;
                            totalCollisionChecks -= reduced;
                            removedCount++;
                            Debug.Log($"[QuestPhysBonePruner] [Pass 3] Cleared colliders from '{pb.gameObject.name}': checks {before} → {after} (saved {reduced}). Running total: {totalCollisionChecks}");
                            progressCallback?.Invoke($"Cleared colliders from PhysBone component on {pb.gameObject.name} to reduce collision checks.");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[QuestPhysBonePruner] Could not clear colliders on {pb.name}: {e.Message}");
                    }
                }

                Debug.Log($"[QuestPhysBonePruner] [Pass 3] After collider trimming: collision checks = {totalCollisionChecks} / {maxChecks}");
            }
            else
            {
                Debug.Log($"[QuestPhysBonePruner] [Pass 3] Collision checks OK: {totalCollisionChecks} / {maxChecks} — no collider trimming needed.");
            }

            // 4. If total collision checks still > maxChecks, prune PhysBones with largest check counts until total <= maxChecks
            pbList.RemoveAll(c => c == null);
            totalCollisionChecks = CalculateTotalCollisionChecks(pbList);
            if (totalCollisionChecks > maxChecks)
            {
                Debug.Log($"[QuestPhysBonePruner] [Pass 4] Still {totalCollisionChecks} collision checks after collider trimming. Pruning PB components...");
                pbList.Sort((a, b) => GetCollisionCheckCount(b).CompareTo(GetCollisionCheckCount(a)));

                for (int i = 0; i < pbList.Count; i++)
                {
                    if (totalCollisionChecks <= maxChecks) break;
                    Component pb = pbList[i];
                    if (pb != null)
                    {
                        int checks = GetCollisionCheckCount(pb);
                        Debug.Log($"[QuestPhysBonePruner] [Pass 4] Removing PhysBone on '{pb.gameObject.name}' ({checks} collision checks). Running total after: {totalCollisionChecks - checks}");
                        Undo.DestroyObjectImmediate(pb);
                        totalCollisionChecks -= checks;
                        removedCount++;
                        progressCallback?.Invoke($"Pruned PhysBone component on {pb.gameObject.name} to enforce <= {maxChecks} collision checks.");
                    }
                }

                if (totalCollisionChecks <= maxChecks)
                    Debug.Log($"[QuestPhysBonePruner] [Pass 4] Collision checks now within limit: {totalCollisionChecks} / {maxChecks}.");
                else
                    Debug.LogWarning($"[QuestPhysBonePruner] [Pass 4] WARNING: Could not reduce collision checks to {maxChecks}. Final: {totalCollisionChecks}. All PBs may be removed by VRChat!");
            }
            else
            {
                Debug.Log($"[QuestPhysBonePruner] [Pass 4] No additional PB pruning needed. Collision checks: {totalCollisionChecks} / {maxChecks}.");
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
