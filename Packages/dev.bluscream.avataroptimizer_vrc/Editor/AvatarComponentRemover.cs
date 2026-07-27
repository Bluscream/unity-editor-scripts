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
    /// Removes platform-incompatible components from avatars based on PlatformProfile rules
    /// </summary>
    public static class AvatarComponentRemover
    {
        public class RemovedComponent
        {
            public GameObject gameObject;
            public string componentType;
            public string gameObjectPath;
        }

        /// <summary>
        /// Removes all platform-incompatible components from the avatar according to profile rules
        /// </summary>
        public static List<RemovedComponent> RemoveIncompatibleComponents(GameObject avatarRoot, PlatformProfile profile, Action<string> progressCallback = null)
        {
            List<RemovedComponent> removed = new List<RemovedComponent>();
            
            if (avatarRoot == null)
            {
                Debug.LogError("[AvatarComponentRemover] Avatar root is null");
                return removed;
            }

            profile = profile ?? PlatformProfile.GetProfile(TargetPlatform.Android, AvatarPerformanceRank.Medium);

            List<GameObject> allGameObjects = avatarRoot.transform.CollectAllGameObjects();
            Debug.Log($"[AvatarComponentRemover] Starting component removal on '{avatarRoot.name}' ({allGameObjects.Count} GameObjects) using profile '{profile.Platform}_{profile.Rank}'.");

            int total = allGameObjects.Count;
            for (int i = 0; i < allGameObjects.Count; i++)
            {
                GameObject go = allGameObjects[i];
                if (go == null) continue;

                progressCallback?.Invoke($"Removing incompatible components ({i + 1}/{total})...");

                Component[] components = go.GetComponents<Component>();

                // Pass: Remove blacklisted / incompatible components
                // Sort components so dependent scripts (e.g. VRCSpatialAudioSource) are removed BEFORE base components (e.g. AudioSource)
                var toRemoveList = components.Where(c => c != null && ShouldRemoveComponent(c, profile)).ToList();
                toRemoveList.Sort((a, b) =>
                {
                    bool isABase = a is AudioSource || a is Transform;
                    bool isBBase = b is AudioSource || b is Transform;
                    if (!isABase && isBBase) return -1;
                    if (isABase && !isBBase) return 1;
                    return 0;
                });

                foreach (Component comp in toRemoveList)
                {
                    if (comp == null) continue;

                    RemovedComponent removedComp = new RemovedComponent
                    {
                        gameObject = go,
                        componentType = comp.GetType().FullName,
                        gameObjectPath = GetGameObjectPath(go)
                    };

                    try
                    {
                        Undo.RegisterCompleteObjectUndo(go, "Remove platform-incompatible component");
                        
                        if (Application.isPlaying)
                            UnityEngine.Object.Destroy(comp);
                        else
                            UnityEngine.Object.DestroyImmediate(comp, true);
                        
                        Debug.Log($"[AvatarComponentRemover] [Pass 2] Removed '{comp.GetType().Name}' from '{GetGameObjectPath(go)}'");
                        removed.Add(removedComp);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[AvatarComponentRemover] Failed to remove '{comp.GetType().Name}' from {go.name}: {e.Message}");
                    }
                }
            }

            // Pass: Prune excess VRCContactSender and VRCContactReceiver components to profile limit
            int maxContacts = profile.MaxContacts;
            List<Component> contactComps = avatarRoot.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && (c.GetType().Name.Contains("VRCContactSender") || c.GetType().Name.Contains("VRCContactReceiver")))
                .ToList();

            if (contactComps.Count > maxContacts)
            {
                Debug.Log($"[AvatarComponentRemover] VRCContact components: {contactComps.Count} > {maxContacts} limit. Pruning {contactComps.Count - maxContacts}.");
                progressCallback?.Invoke($"Pruning excess VRCContact components ({contactComps.Count} -> {maxContacts})...");
                for (int i = maxContacts; i < contactComps.Count; i++)
                {
                    Component c = contactComps[i];
                    if (c != null)
                    {
                        Debug.Log($"[AvatarComponentRemover] Removing '{c.GetType().Name}' from '{GetGameObjectPath(c.gameObject)}'");
                        removed.Add(new RemovedComponent
                        {
                            gameObject = c.gameObject,
                            componentType = c.GetType().FullName,
                            gameObjectPath = GetGameObjectPath(c.gameObject)
                        });
                        Undo.DestroyObjectImmediate(c);
                    }
                }
            }

            // Pass: Prune excess Constraint components to profile limit
            int maxConstraints = profile.MaxConstraints;
            List<Component> constraintComps = avatarRoot.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && c.GetType().Name.ToLowerInvariant().Contains("constraint"))
                .ToList();

            if (constraintComps.Count > maxConstraints)
            {
                Debug.Log($"[AvatarComponentRemover] Constraint components: {constraintComps.Count} > {maxConstraints} limit. Pruning {constraintComps.Count - maxConstraints}.");
                progressCallback?.Invoke($"Pruning excess Constraints ({constraintComps.Count} -> {maxConstraints})...");
                for (int i = maxConstraints; i < constraintComps.Count; i++)
                {
                    Component c = constraintComps[i];
                    if (c != null)
                    {
                        removed.Add(new RemovedComponent
                        {
                            gameObject = c.gameObject,
                            componentType = c.GetType().FullName,
                            gameObjectPath = GetGameObjectPath(c.gameObject)
                        });
                        Undo.DestroyObjectImmediate(c);
                    }
                }
            }

            // Pass: Prune excess TrailRenderers
            int maxTrails = profile.MaxTrailRenderers;
            List<TrailRenderer> trailComps = avatarRoot.GetComponentsInChildren<TrailRenderer>(true).ToList();
            if (trailComps.Count > maxTrails)
            {
                for (int i = maxTrails; i < trailComps.Count; i++)
                {
                    if (trailComps[i] != null)
                    {
                        removed.Add(new RemovedComponent { gameObject = trailComps[i].gameObject, componentType = typeof(TrailRenderer).FullName, gameObjectPath = GetGameObjectPath(trailComps[i].gameObject) });
                        Undo.DestroyObjectImmediate(trailComps[i]);
                    }
                }
            }

            // Pass: Prune excess LineRenderers
            int maxLines = profile.MaxLineRenderers;
            List<LineRenderer> lineComps = avatarRoot.GetComponentsInChildren<LineRenderer>(true).ToList();
            if (lineComps.Count > maxLines)
            {
                for (int i = maxLines; i < lineComps.Count; i++)
                {
                    if (lineComps[i] != null)
                    {
                        removed.Add(new RemovedComponent { gameObject = lineComps[i].gameObject, componentType = typeof(LineRenderer).FullName, gameObjectPath = GetGameObjectPath(lineComps[i].gameObject) });
                        Undo.DestroyObjectImmediate(lineComps[i]);
                    }
                }
            }

            // Pass: Prune excess ParticleSystem components & cap maxParticles
            int maxParticleSys = profile.MaxParticleSystems;
            List<ParticleSystem> particleComps = avatarRoot.GetComponentsInChildren<ParticleSystem>(true).ToList();
            if (particleComps.Count > maxParticleSys)
            {
                for (int i = maxParticleSys; i < particleComps.Count; i++)
                {
                    if (particleComps[i] != null)
                    {
                        removed.Add(new RemovedComponent { gameObject = particleComps[i].gameObject, componentType = typeof(ParticleSystem).FullName, gameObjectPath = GetGameObjectPath(particleComps[i].gameObject) });
                        Undo.DestroyObjectImmediate(particleComps[i]);
                    }
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

            Debug.Log($"[AvatarComponentRemover] Done. Total removed: {removed.Count} component(s).");
            return removed;
        }

        /// <summary>
        /// Legacy overload for backward compatibility
        /// </summary>
        public static List<RemovedComponent> RemoveIncompatibleComponents(GameObject avatarRoot, Action<string> progressCallback = null)
        {
            return RemoveIncompatibleComponents(avatarRoot, PlatformProfile.GetProfile(TargetPlatform.Android, AvatarPerformanceRank.Medium), progressCallback);
        }

        /// <summary>
        /// Determines if a component should be removed based on the given PlatformProfile
        /// </summary>
        public static bool ShouldRemoveComponent(Component comp, PlatformProfile profile)
        {
            if (comp == null) return false;

            Type compType = comp.GetType();
            string typeName = compType.Name;
            string typeFullName = compType.FullName ?? typeName;

            // 1. Whitelist check: If component is explicitly whitelisted, keep it
            if (profile.WhitelistedComponentNames.Contains(typeName) || profile.WhitelistedComponentNames.Contains(typeFullName))
                return false;

            // 2. Blacklist check: If component is in profile blacklist, remove it
            if (profile.BlacklistedComponentNames.Contains(typeName) || profile.BlacklistedComponentNames.Contains(typeFullName))
                return true;

            // 3. Custom profile method check
            if (profile.ShouldRemoveComponentCustom(comp))
                return true;

            string typeNameLower = typeFullName.ToLowerInvariant();

            // Dynamic Bones
            if (typeNameLower.Contains("dynamicbone") || typeName.Contains("DynamicBone"))
                return true;

            // Cloth
            if (comp is Cloth && profile.MaxClothComponents <= 0)
                return true;

            // Camera (avatars only)
            if (comp is Camera)
                return true;

            // Light (avatars only)
            if (comp is Light && profile.MaxLights <= 0)
                return true;

            // AudioSource / VRCSpatialAudioSource (avatars only)
            if ((comp is AudioSource || typeName.Contains("AudioSource")) && profile.MaxAudioSources <= 0)
                return true;

            // Rigidbody
            if (comp is Rigidbody && profile.MaxRigidbodies <= 0)
                return true;

            // Joints
            if (comp is Joint || compType.IsSubclassOf(typeof(Joint)))
                return true;

            // Particle Systems
            if (comp is ParticleSystem && profile.MaxParticleSystems <= 0)
                return true;

            // TrailRenderer
            if (comp is TrailRenderer && profile.MaxTrailRenderers <= 0)
                return true;

            // LineRenderer
            if (comp is LineRenderer && profile.MaxLineRenderers <= 0)
                return true;

            // Physics Colliders (exclude PhysBoneColliders)
            if (comp is Collider && !typeName.Contains("VRCPhysBoneCollider") && profile.MaxPhysicsColliders <= 0)
                return true;

            // Constraints (non-VRChat)
            if (typeNameLower.Contains("constraint") && !typeNameLower.Contains("vrchat"))
                return true;

            // FinalIK
            if (typeNameLower.Contains("finalik") || typeNameLower.Contains("rootmotion.finalik"))
                return true;

            // Post-processing components
            if (typeNameLower.Contains("postprocess") || typeNameLower.Contains("postprocesslayer"))
                return true;

            return false;
        }

    }
}
