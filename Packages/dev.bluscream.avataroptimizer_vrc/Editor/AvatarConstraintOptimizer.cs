using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static Bluscream.TransformExtensions;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Dedicated optimizer for Unity and VRChat Constraint components.
    /// </summary>
    public static class AvatarConstraintOptimizer
    {
        /// <summary>
        /// Prunes excess Constraint components to fit within profile.MaxConstraints limit.
        /// </summary>
        public static int PruneConstraints(GameObject avatarRoot, int maxConstraints, Action<string> progressCallback = null)
        {
            if (avatarRoot == null) return 0;

            List<Component> constraintComps = avatarRoot.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && c.GetType().Name.ToLowerInvariant().Contains("constraint"))
                .ToList();

            if (constraintComps.Count <= maxConstraints) return 0;

            int prunedCount = constraintComps.Count - maxConstraints;
            Debug.Log($"[AvatarConstraintOptimizer] Constraint components: {constraintComps.Count} > {maxConstraints} limit. Pruning {prunedCount}.");
            progressCallback?.Invoke($"Pruning excess Constraints ({constraintComps.Count} -> {maxConstraints})...");

            for (int i = maxConstraints; i < constraintComps.Count; i++)
            {
                Component c = constraintComps[i];
                if (c != null)
                {
                    Debug.Log($"[AvatarConstraintOptimizer] Pruning '{c.GetType().Name}' from '{GetGameObjectPath(c.gameObject)}'");
                    Undo.DestroyObjectImmediate(c);
                }
            }

            return prunedCount;
        }
    }
}
