using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Dedicated optimizer for enforcing dynamic Light component limits.
    /// </summary>
    public static class AvatarLightOptimizer
    {
        /// <summary>
        /// Disables excess dynamic lights on the avatar when their count exceeds maxLights.
        /// Lights on active root or main body are prioritized over secondary prop lights.
        /// </summary>
        public static void OptimizeLights(GameObject avatarRoot, int maxLights, Action<string> progressCallback = null)
        {
            if (avatarRoot == null || maxLights == int.MaxValue) return;

            Light[] lights = avatarRoot.GetComponentsInChildren<Light>(true).Where(l => l != null).ToArray();

            // VRChat's performance stats count Light *components*, not enabled ones, so disabling a light
            // does not move the metric. Components have to go for the avatar to meet the limit.
            int componentCount = lights.Length;
            if (componentCount <= maxLights) return;

            int toRemove = componentCount - maxLights;
            progressCallback?.Invoke($"Removing excess dynamic lights ({componentCount} -> max {maxLights})...");
            Debug.Log($"[AvatarLightOptimizer] Light components {componentCount} > max {maxLights}. Removing {toRemove} (deepest first).");

            // Deepest first: prop and accessory lights before anything near the avatar root.
            var lightsToRemove = lights
                .OrderByDescending(l => GetHierarchyDepth(l.transform))
                .Take(toRemove)
                .ToList();

            foreach (Light light in lightsToRemove)
            {
                Debug.Log($"[AvatarLightOptimizer] Removing Light component on '{light.gameObject.name}' (type {light.type}, {(light.enabled ? "enabled" : "disabled")}).");
                Undo.DestroyObjectImmediate(light);
            }

            int remaining = avatarRoot.GetComponentsInChildren<Light>(true).Count(l => l != null);
            Debug.Log($"[AvatarLightOptimizer] Light components now {remaining} / {maxLights}.");
        }

        private static int GetHierarchyDepth(Transform t)
        {
            int depth = 0;
            while (t != null) { depth++; t = t.parent; }
            return depth;
        }
    }
}
