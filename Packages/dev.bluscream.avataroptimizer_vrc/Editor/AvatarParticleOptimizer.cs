using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Dedicated optimizer for Particle Systems, TrailRenderers, and LineRenderers.
    /// </summary>
    public static class AvatarParticleOptimizer
    {
        /// <summary>
        /// Optimizes particle systems, trail renderers, and line renderers to fit profile limits.
        /// </summary>
        public static void OptimizeParticleSystems(GameObject avatarRoot, PlatformProfile profile, Action<string> progressCallback = null)
        {
            if (avatarRoot == null || profile == null) return;

            // Step 1: Prune excess TrailRenderers
            int maxTrails = profile.MaxTrailRenderers;
            List<TrailRenderer> trailComps = avatarRoot.GetComponentsInChildren<TrailRenderer>(true).ToList();
            if (trailComps.Count > maxTrails)
            {
                for (int i = maxTrails; i < trailComps.Count; i++)
                {
                    if (trailComps[i] != null) Undo.DestroyObjectImmediate(trailComps[i]);
                }
            }

            // Step 2: Prune excess LineRenderers
            int maxLines = profile.MaxLineRenderers;
            List<LineRenderer> lineComps = avatarRoot.GetComponentsInChildren<LineRenderer>(true).ToList();
            if (lineComps.Count > maxLines)
            {
                for (int i = maxLines; i < lineComps.Count; i++)
                {
                    if (lineComps[i] != null) Undo.DestroyObjectImmediate(lineComps[i]);
                }
            }

            // Step 3: Prune excess ParticleSystems and cap maxParticles
            int maxParticleSys = profile.MaxParticleSystems;
            List<ParticleSystem> particleComps = avatarRoot.GetComponentsInChildren<ParticleSystem>(true).ToList();
            if (particleComps.Count > maxParticleSys)
            {
                for (int i = maxParticleSys; i < particleComps.Count; i++)
                {
                    if (particleComps[i] != null) Undo.DestroyObjectImmediate(particleComps[i]);
                }
                particleComps = particleComps.Take(maxParticleSys).ToList();
            }

            if (particleComps.Count > 0 && profile.MaxActiveParticles < int.MaxValue)
            {
                int totalActiveParticles = particleComps.Sum(ps => ps != null ? ps.main.maxParticles : 0);
                if (totalActiveParticles > profile.MaxActiveParticles)
                {
                    int budgetPerPs = Math.Max(1, profile.MaxActiveParticles / particleComps.Count);
                    foreach (var ps in particleComps)
                    {
                        if (ps == null) continue;
                        var main = ps.main;
                        if (main.maxParticles > budgetPerPs)
                        {
                            Undo.RecordObject(ps, "Cap Particle System Max Particles");
                            main.maxParticles = budgetPerPs;
                        }
                    }
                }
            }
        }
    }
}
