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

            Debug.Log($"[AvatarPhysBonePruner] Found {pbList.Count} PhysBone component(s) and {pbColliders.Count} PhysBone Collider(s) on '{avatarRoot.name}'.");

            int removedCount = 0;
            int totalCollisionChecks = 0;
            int maxChecks = profile.MaxPhysBoneCollisionChecks;

            // 1. Remove excess Colliders first if over budget
            int targetColliders = profile.MaxPhysBoneColliders;
            if (pbColliders.Count > targetColliders)
            {
                Debug.Log($"[AvatarPhysBonePruner] [Pass 1] Colliders {pbColliders.Count} > limit {targetColliders}. Removing {pbColliders.Count - targetColliders} collider(s).");
                for (int i = pbColliders.Count - 1; i >= targetColliders; i--)
                {
                    if (pbColliders[i] != null)
                    {
                        Debug.Log($"[AvatarPhysBonePruner] [Pass 1] Removing collider on '{pbColliders[i].gameObject.name}'");
                        progressCallback?.Invoke($"Pruning PhysBone Collider {pbColliders[i].gameObject.name}...");
                        Undo.DestroyObjectImmediate(pbColliders[i]);
                        removedCount++;
                    }
                }
            }
            else
            {
                Debug.Log($"[AvatarPhysBonePruner] [Pass 1] Colliders OK: {pbColliders.Count} / {targetColliders} limit.");
            }

            // 2. Remove excess PhysBone components if over budget
            int targetComponents = profile.MaxPhysBoneComponents;
            if (pbList.Count > targetComponents)
            {
                int toRemove = pbList.Count - targetComponents;
                Debug.Log($"[AvatarPhysBonePruner] [Pass 2] PhysBones {pbList.Count} > limit {targetComponents}. Pruning {toRemove} component(s).");
                progressCallback?.Invoke($"PhysBones count ({pbList.Count}) exceeds profile limit ({targetComponents}). Pruning {toRemove} components...");

                pbList = pbList.OrderBy(pb => pb.transform.GetHierarchyDepth()).ToList();

                for (int i = pbList.Count - 1; i >= targetComponents; i--)
                {
                    if (pbList[i] != null)
                    {
                        Debug.Log($"[AvatarPhysBonePruner] [Pass 2] Removing PhysBone on '{pbList[i].gameObject.name}'");
                        Undo.DestroyObjectImmediate(pbList[i]);
                        pbList.RemoveAt(i);
                        removedCount++;
                    }
                }
            }
            else
            {
                Debug.Log($"[AvatarPhysBonePruner] [Pass 2] PhysBone component count OK: {pbList.Count} / {targetComponents} limit.");
            }

            // 3. Evaluate Collision Checks
            totalCollisionChecks = CalculateTotalCollisionChecks(pbList);
            Debug.Log($"[AvatarPhysBonePruner] [Pass 3] Collision checks: {totalCollisionChecks} / {maxChecks} limit across {pbList.Count} remaining PBs.");

            if (totalCollisionChecks > maxChecks)
            {
                progressCallback?.Invoke($"Total PhysBone Collision Checks ({totalCollisionChecks}) exceeds limit ({maxChecks}). Trimming colliders...");
                var sortedPBs = pbList.OrderByDescending(pb => GetCollisionCheckCount(pb)).ToList();
                foreach (Component pb in sortedPBs)
                {
                    if (pb == null) continue;
                    int before = GetCollisionCheckCount(pb);
                    if (before == 0) continue;

                    try
                    {
                        SerializedObject so = new SerializedObject(pb);
                        SerializedProperty collidersProp = so.FindProperty("colliders");
                        if (collidersProp != null && collidersProp.isArray && collidersProp.arraySize > 0)
                        {
                            collidersProp.ClearArray();
                            so.ApplyModifiedProperties();

                            int after = GetCollisionCheckCount(pb);
                            int reduced = before - after;
                            totalCollisionChecks -= reduced;

                            Debug.Log($"[AvatarPhysBonePruner] [Pass 3] Cleared colliders from '{pb.gameObject.name}': checks {before} → {after} (saved {reduced}). Running total: {totalCollisionChecks}");

                            if (totalCollisionChecks <= maxChecks) break;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[AvatarPhysBonePruner] Could not clear colliders on {pb.name}: {e.Message}");
                    }
                }

                Debug.Log($"[AvatarPhysBonePruner] [Pass 3] After collider trimming: collision checks = {totalCollisionChecks} / {maxChecks}");
            }

            Debug.Log($"[AvatarPhysBonePruner] Pruned {removedCount} PhysBone components/colliders to meet target rank '{profile.Rank}'.");
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
                return root.CountDescendants();
            }
            catch
            {
                return 1;
            }
        }

    }
}
