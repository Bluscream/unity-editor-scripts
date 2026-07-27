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

            // Step 1: Prune excess TrailRenderers (deepest in hierarchy first — usually accessory effects)
            PruneExcess(avatarRoot.GetComponentsInChildren<TrailRenderer>(true), profile.MaxTrailRenderers, "TrailRenderer");

            // Step 2: Prune excess LineRenderers
            PruneExcess(avatarRoot.GetComponentsInChildren<LineRenderer>(true), profile.MaxLineRenderers, "LineRenderer");

            // Step 3: Prune excess ParticleSystems
            List<ParticleSystem> particleComps = avatarRoot.GetComponentsInChildren<ParticleSystem>(true).Where(ps => ps != null).ToList();
            if (particleComps.Count > profile.MaxParticleSystems)
            {
                progressCallback?.Invoke($"Pruning excess Particle Systems ({particleComps.Count} -> {profile.MaxParticleSystems})...");
                particleComps = particleComps.OrderBy(ps => GetDepth(ps.transform)).ToList();
                while (particleComps.Count > profile.MaxParticleSystems)
                {
                    ParticleSystem ps = particleComps[particleComps.Count - 1];
                    particleComps.RemoveAt(particleComps.Count - 1);
                    if (ps == null) continue;
                    Debug.Log($"[AvatarParticleOptimizer] Pruning ParticleSystem on '{ps.gameObject.name}'");
                    Undo.DestroyObjectImmediate(ps);
                }
            }

            // Step 4: Cap total active particles across surviving systems
            if (particleComps.Count > 0 && profile.MaxActiveParticles < int.MaxValue)
            {
                int totalActiveParticles = particleComps.Sum(ps => ps != null ? ps.main.maxParticles : 0);
                if (totalActiveParticles > profile.MaxActiveParticles)
                {
                    int budgetPerPs = Math.Max(1, profile.MaxActiveParticles / Math.Max(1, particleComps.Count));
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

            // Step 5: Disable trail and collision modules when the profile forbids them
            foreach (var ps in particleComps)
            {
                if (ps == null) continue;

                if (!profile.ParticleTrailsEnabledAllowed && ps.trails.enabled)
                {
                    Undo.RecordObject(ps, "Disable Particle Trails");
                    var trails = ps.trails;
                    trails.enabled = false;
                    Debug.Log($"[AvatarParticleOptimizer] Disabled trails module on '{ps.gameObject.name}' (not allowed by profile).");
                }

                if (!profile.ParticleCollisionEnabledAllowed && ps.collision.enabled)
                {
                    Undo.RecordObject(ps, "Disable Particle Collision");
                    var collision = ps.collision;
                    collision.enabled = false;
                    Debug.Log($"[AvatarParticleOptimizer] Disabled collision module on '{ps.gameObject.name}' (not allowed by profile).");
                }
            }

            // Step 6: Enforce mesh particle polygon budget (mesh tris × max particles, summed)
            if (profile.MaxMeshParticlePolyCount < int.MaxValue)
            {
                EnforceMeshParticlePolyBudget(particleComps, profile.MaxMeshParticlePolyCount);
            }
        }

        private static void EnforceMeshParticlePolyBudget(List<ParticleSystem> particleComps, int maxPolys)
        {
            var meshSystems = new List<(ParticleSystem ps, ParticleSystemRenderer renderer, int meshTris)>();
            long totalPolys = 0;

            foreach (var ps in particleComps)
            {
                if (ps == null) continue;
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer == null || renderer.renderMode != ParticleSystemRenderMode.Mesh || renderer.mesh == null) continue;

                int meshTris = (int)(renderer.mesh.GetIndexCount(0) / 3);
                for (int sub = 1; sub < renderer.mesh.subMeshCount; sub++)
                    meshTris += (int)(renderer.mesh.GetIndexCount(sub) / 3);

                meshSystems.Add((ps, renderer, meshTris));
                totalPolys += (long)meshTris * ps.main.maxParticles;
            }

            if (meshSystems.Count == 0 || totalPolys <= maxPolys) return;

            if (maxPolys <= 0)
            {
                // Mesh particles not allowed at all — fall back to billboards
                foreach (var (ps, renderer, _) in meshSystems)
                {
                    Undo.RecordObject(renderer, "Disable Mesh Particles");
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                    Debug.Log($"[AvatarParticleOptimizer] Switched mesh particles to billboard on '{ps.gameObject.name}' (mesh particles not allowed by profile).");
                }
                return;
            }

            // Scale down maxParticles proportionally to fit the polygon budget
            double factor = (double)maxPolys / totalPolys;
            foreach (var (ps, _, meshTris) in meshSystems)
            {
                var main = ps.main;
                int capped = Math.Max(1, (int)(main.maxParticles * factor));
                if (capped < main.maxParticles)
                {
                    Undo.RecordObject(ps, "Cap Mesh Particle Count");
                    main.maxParticles = capped;
                    Debug.Log($"[AvatarParticleOptimizer] Capped mesh particle count on '{ps.gameObject.name}' to {capped} ({meshTris} tris/mesh) to fit {maxPolys} poly budget.");
                }
            }
        }

        private static void PruneExcess<T>(T[] components, int max, string label) where T : Component
        {
            List<T> comps = components.Where(c => c != null).ToList();
            if (comps.Count <= max) return;

            comps = comps.OrderBy(c => GetDepth(c.transform)).ToList();
            while (comps.Count > max)
            {
                T c = comps[comps.Count - 1]; // deepest first
                comps.RemoveAt(comps.Count - 1);
                if (c == null) continue;
                Debug.Log($"[AvatarParticleOptimizer] Pruning {label} on '{c.gameObject.name}'");
                Undo.DestroyObjectImmediate(c);
            }
        }

        private static int GetDepth(Transform t)
        {
            int depth = 0;
            while (t.parent != null) { depth++; t = t.parent; }
            return depth;
        }
    }
}
