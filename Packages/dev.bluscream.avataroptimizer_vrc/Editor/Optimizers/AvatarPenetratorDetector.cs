using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Detects DPS (Raliv), TPS, and VRCFury SPS penetrators and orifices on avatars.
    /// Excludes them from mesh merging/decimation on PC, and flags them for stripping on Mobile.
    /// </summary>
    public static class AvatarPenetratorDetector
    {
        public static bool IsPenetratorLight(Light light)
        {
            if (light == null) return false;
            // Raliv DPS tip lights use point light with exact 0.01f range
            if (light.type == LightType.Point && Mathf.Approximately(light.range, 0.01f)) return true;
            if (light.gameObject.name.IndexOf("Orifice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                light.gameObject.name.IndexOf("Penetrator", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        public static bool IsPenetratorComponent(Component c)
        {
            if (c == null) return false;
            string name = c.GetType().Name;
            if (name.IndexOf("SpsPenetrator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("SpsOrifice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("VRCFurySps", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Raliv", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Penetrator", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        public static bool IsPenetratorRenderer(Renderer r)
        {
            if (r == null) return false;

            // Check if GameObject or parent name has penetrator/orifice keywords
            Transform t = r.transform;
            while (t != null)
            {
                string n = t.gameObject.name;
                if (n.IndexOf("Penetrator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Orifice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Raliv", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("SPS_", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                // Check for penetrator lights or components on this transform
                if (t.GetComponents<Light>().Any(IsPenetratorLight)) return true;
                if (t.GetComponents<Component>().Any(IsPenetratorComponent)) return true;

                t = t.parent;
            }

            return false;
        }

        public static HashSet<Renderer> CollectPenetratorRenderers(GameObject avatarRoot)
        {
            HashSet<Renderer> set = new HashSet<Renderer>();
            if (avatarRoot == null) return set;

            foreach (var r in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (IsPenetratorRenderer(r))
                    set.Add(r);
            }

            if (set.Count > 0)
                Debug.Log($"[AvatarPenetratorDetector] Detected {set.Count} DPS/TPS/SPS penetrator renderer(s) on '{avatarRoot.name}'.");

            return set;
        }
    }
}
