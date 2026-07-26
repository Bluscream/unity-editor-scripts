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
                string typeName = c.GetType().FullName;
                if (typeName.EndsWith("VRCPhysBone")) pbList.Add(c);
                else if (typeName.Contains("VRCPhysBoneCollider")) pbColliders.Add(c);
            }

            int removedCount = 0;

            // 1. Remove excess Colliders first if over budget
            if (pbColliders.Count > profile.MaxPhysBoneColliders)
            {
                int toRemove = pbColliders.Count - profile.MaxPhysBoneColliders;
                for (int i = pbColliders.Count - 1; i >= profile.MaxPhysBoneColliders; i--)
                {
                    progressCallback?.Invoke($"Pruning PhysBone Collider {pbColliders[i].gameObject.name}...");
                    Undo.DestroyObjectImmediate(pbColliders[i]);
                    removedCount++;
                }
            }

            // 2. Remove excess PhysBone components if over budget
            if (pbList.Count > profile.MaxPhysBoneComponents)
            {
                int toRemove = pbList.Count - profile.MaxPhysBoneComponents;
                progressCallback?.Invoke($"PhysBones count ({pbList.Count}) exceeds rank limit ({profile.MaxPhysBoneComponents}). Pruning {toRemove} components...");

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
