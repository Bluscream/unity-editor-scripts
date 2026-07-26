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

            // 3. Trim collider references on remaining PhysBones to guarantee collision checks <= 64
            foreach (Component pb in pbList)
            {
                if (pb == null) continue;
                try
                {
                    var collidersProp = pb.GetType().GetProperty("colliders") ?? pb.GetType().GetProperty("Colliders");
                    if (collidersProp != null && collidersProp.GetValue(pb) is System.Collections.IList list && list.Count > 2)
                    {
                        Undo.RecordObject(pb, "Trim PhysBone Colliders");
                        while (list.Count > 2)
                        {
                            list.RemoveAt(list.Count - 1);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[QuestPhysBonePruner] Could not trim collider list on {pb.name}: {e.Message}");
                }
            }

            Debug.Log($"[QuestPhysBonePruner] Pruned {removedCount} PhysBone components/colliders to meet target rank '{profile.Rank}'.");
            return removedCount;
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
