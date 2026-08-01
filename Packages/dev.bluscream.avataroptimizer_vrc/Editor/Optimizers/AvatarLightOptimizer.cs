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

            Light[] lights = avatarRoot.GetComponentsInChildren<Light>(true);
            int currentLights = lights.Count(l => l != null && l.enabled);

            if (currentLights <= maxLights) return;

            progressCallback?.Invoke($"Optimizing dynamic lights ({currentLights} -> max {maxLights})...");
            Debug.Log($"[AvatarLightOptimizer] Light count {currentLights} > max {maxLights}. Disabling excess lights.");

            // Order by importance: enabled lights on lower depth/hierarchies prioritized
            var lightsToDisable = lights
                .Where(l => l != null && l.enabled)
                .OrderByDescending(l => GetHierarchyDepth(l.transform))
                .Take(currentLights - maxLights);

            foreach (Light light in lightsToDisable)
            {
                Undo.RecordObject(light, "Disable Excess Light");
                light.enabled = false;
                Debug.Log($"[AvatarLightOptimizer] Disabled Light component on '{light.gameObject.name}'.");
            }
        }

        private static int GetHierarchyDepth(Transform t)
        {
            int depth = 0;
            while (t != null) { depth++; t = t.parent; }
            return depth;
        }
    }
}
