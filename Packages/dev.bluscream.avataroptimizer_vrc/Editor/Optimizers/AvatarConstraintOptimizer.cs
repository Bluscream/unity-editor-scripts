using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static Bluscream.Utils;
using static Bluscream.TransformExtensions;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Dedicated optimizer for Unity and VRChat Constraint components.
    /// </summary>
    public static class AvatarConstraintOptimizer
    {
        private static readonly BluLog Log = BluLog.Get("AvatarConstraintOptimizer");

        /// <summary>
        /// Prunes excess Constraint components to fit within profile.MaxConstraints limit.
        /// </summary>
        public static int PruneConstraints(GameObject avatarRoot, int maxConstraints, Action<string> progressCallback = null)
        {
            if (avatarRoot == null) return 0;

            List<Component> constraintComps = avatarRoot.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && c.GetType().Name.ToLowerInvariant().Contains("constraint"))
                // Shallowest first: the loop below prunes the tail, so deep accessory/detail components
                // are dropped before ones near the avatar root. Mirrors AvatarPhysBonePruner's ordering.
                .OrderBy(c => c.transform.GetHierarchyDepth())
                .ToList();

            if (constraintComps.Count <= maxConstraints) return 0;

            int prunedCount = constraintComps.Count - maxConstraints;
            Log.Info($"Constraint components: {constraintComps.Count} > {maxConstraints} limit. Pruning {prunedCount}.");
            progressCallback?.Invoke($"Pruning excess Constraints ({constraintComps.Count} -> {maxConstraints})...");

            for (int i = maxConstraints; i < constraintComps.Count; i++)
            {
                Component c = constraintComps[i];
                if (c != null)
                {
                    Log.Info($"Pruning '{c.GetType().Name}' from '{GetGameObjectPath(c.gameObject)}'");
                    Undo.DestroyObjectImmediate(c);
                }
            }

            return prunedCount;
        }
    }
}
