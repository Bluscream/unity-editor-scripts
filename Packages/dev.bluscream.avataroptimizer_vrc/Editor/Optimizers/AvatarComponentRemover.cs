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

            // Component removal can fail on dependency chains (e.g. a Joint that [RequireComponent]s a
            // Rigidbody: DestroyImmediate on the Rigidbody logs an error and silently leaves it alive).
            // The base-first sort handles the common cases; the fixpoint loop sweeps up whatever remains
            // once its dependents were removed in an earlier pass.
            const int maxPasses = 5;
            for (int pass = 1; pass <= maxPasses; pass++)
            {
                int removedThisPass = 0;

                int total = allGameObjects.Count;
                for (int i = 0; i < allGameObjects.Count; i++)
                {
                    GameObject go = allGameObjects[i];
                    if (go == null) continue;

                    progressCallback?.Invoke($"Removing incompatible components (pass {pass}, {i + 1}/{total})...");

                    Component[] components = go.GetComponents<Component>();

                    // Sort components so dependent scripts (e.g. VRCSpatialAudioSource, Joints) are removed
                    // BEFORE the base components they require (AudioSource, Rigidbody, Colliders).
                    var toRemoveList = components.Where(c => c != null && ShouldRemoveComponent(c, profile)).ToList();
                    toRemoveList.Sort((a, b) => GetRemovalOrder(a).CompareTo(GetRemovalOrder(b)));

                    foreach (Component comp in toRemoveList)
                    {
                        if (comp == null) continue;

                        RemovedComponent removedComp = new RemovedComponent
                        {
                            gameObject = go,
                            componentType = comp.GetType().FullName,
                            gameObjectPath = GetGameObjectPath(go)
                        };
                        string compTypeName = comp.GetType().Name;

                        try
                        {
                            // Undo.DestroyObjectImmediate records the destruction properly so it can be
                            // reverted (RegisterCompleteObjectUndo + DestroyImmediate does not reliably
                            // restore destroyed components).
                            if (Application.isPlaying)
                                UnityEngine.Object.Destroy(comp);
                            else
                                Undo.DestroyObjectImmediate(comp);

                            // Destruction does not throw on dependency failures — it logs an error
                            // and leaves the component alive. Only count it if it is actually gone.
                            if (comp == null)
                            {
                                Debug.Log($"[AvatarComponentRemover] [Pass {pass}] Removed '{compTypeName}' from '{GetGameObjectPath(go)}'");
                                removed.Add(removedComp);
                                removedThisPass++;
                            }
                            else if (pass == maxPasses)
                            {
                                Debug.LogWarning($"[AvatarComponentRemover] Could not remove '{compTypeName}' from '{GetGameObjectPath(go)}' — another component still depends on it.");
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[AvatarComponentRemover] Failed to remove '{compTypeName}' from {go.name}: {e.Message}");
                        }
                    }
                }

                if (removedThisPass == 0) break; // fixpoint reached
            }

            // Delegate dedicated pruning passes to specialized optimizers
            AvatarContactOptimizer.PruneContacts(avatarRoot, profile.MaxContacts, progressCallback);
            AvatarConstraintOptimizer.PruneConstraints(avatarRoot, profile.MaxConstraints, progressCallback);
            AvatarParticleOptimizer.OptimizeParticleSystems(avatarRoot, profile, progressCallback);

            // Prune components that are allowed but exceed their profile count limits
            PruneExcessComponents<Animator>(avatarRoot, profile.MaxAnimators, removed, progressCallback, skipRoot: true);
            PruneExcessComponents<Light>(avatarRoot, profile.MaxLights, removed, progressCallback);
            PruneExcessComponents<AudioSource>(avatarRoot, profile.MaxAudioSources, removed, progressCallback);
            PruneExcessComponents<Cloth>(avatarRoot, profile.MaxClothComponents, removed, progressCallback);

            Debug.Log($"[AvatarComponentRemover] Done. Total removed: {removed.Count} component(s).");
            return removed;
        }

        /// <summary>
        /// Prunes components of type T exceeding the given limit, deepest in the hierarchy first.
        /// With skipRoot, components on the avatar root itself (e.g. the main Animator) are never pruned.
        /// </summary>
        private static void PruneExcessComponents<T>(GameObject avatarRoot, int max, List<RemovedComponent> removed, Action<string> progressCallback, bool skipRoot = false) where T : Component
        {
            if (max == int.MaxValue) return;

            List<T> comps = avatarRoot.GetComponentsInChildren<T>(true)
                .Where(c => c != null && !(skipRoot && c.gameObject == avatarRoot))
                .OrderBy(c => c.transform.GetHierarchyDepth())
                .ToList();

            int allowed = skipRoot ? Math.Max(0, max - avatarRoot.GetComponents<T>().Length) : max;
            if (comps.Count <= allowed) return;

            string label = typeof(T).Name;
            progressCallback?.Invoke($"Pruning excess {label} components ({comps.Count} -> {allowed})...");
            Debug.Log($"[AvatarComponentRemover] {label} count {comps.Count} > limit {allowed}. Pruning deepest-first.");

            while (comps.Count > allowed)
            {
                T c = comps[comps.Count - 1]; // deepest first
                comps.RemoveAt(comps.Count - 1);
                if (c == null) continue;

                // AudioSources may have dependents ([RequireComponent]) like VRCSpatialAudioSource — remove those first
                if (c is AudioSource)
                {
                    foreach (Component sibling in c.GetComponents<Component>())
                    {
                        if (sibling != null && sibling != c && sibling.GetType().Name.Contains("AudioSource"))
                            Undo.DestroyObjectImmediate(sibling);
                    }
                }

                removed.Add(new RemovedComponent
                {
                    gameObject = c.gameObject,
                    componentType = c.GetType().FullName,
                    gameObjectPath = GetGameObjectPath(c.gameObject)
                });
                Debug.Log($"[AvatarComponentRemover] Pruning excess {label} on '{GetGameObjectPath(c.gameObject)}'");
                Undo.DestroyObjectImmediate(c);
            }
        }

        /// <summary>
        /// Convenience overload using default Mobile profile.
        /// </summary>
        public static List<RemovedComponent> RemoveIncompatibleComponents(GameObject avatarRoot, Action<string> progressCallback = null)
        {
            return RemoveIncompatibleComponents(avatarRoot, PlatformProfile.GetProfile(TargetPlatform.Android, AvatarPerformanceRank.Medium), progressCallback);
        }

        /// <summary>
        /// Removal ordering: dependent components (lower value) must be destroyed before the
        /// base components they [RequireComponent] (higher value).
        /// </summary>
        private static int GetRemovalOrder(Component c)
        {
            if (c is Joint) return 0;               // Joints require Rigidbody
            if (c is AudioSource) return 2;          // VRCSpatialAudioSource & co. require AudioSource
            if (c is Rigidbody) return 2;
            if (c is Collider) return 2;
            return 1;                                // everything else in between
        }

        /// <summary>
        /// Determines if a component should be removed based on the given PlatformProfile.
        /// Platform-specific rules (e.g. the Quest component whitelist) live in the profile's
        /// blacklist / ShouldRemoveComponentCustom — this method only applies profile-limit rules.
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

            // DPS/SPS Penetrator components and tip lights are not supported on Mobile/Quest targets
            if (profile.Platform != TargetPlatform.PC && (AvatarPenetratorDetector.IsPenetratorComponent(comp) || (comp is Light lightComp && AvatarPenetratorDetector.IsPenetratorLight(lightComp))))
                return true;

            // 2. Blacklist check: If component is in profile blacklist, remove it
            if (profile.BlacklistedComponentNames.Contains(typeName) || profile.BlacklistedComponentNames.Contains(typeFullName))
                return true;

            // 3. Custom profile method check (platform-specific rules live here)
            if (profile.ShouldRemoveComponentCustom(comp))
                return true;

            // 4. Profile-limit rules: only remove component classes the profile allows zero of
            if (comp is Cloth && profile.MaxClothComponents <= 0)
                return true;

            if (comp is Light && profile.MaxLights <= 0)
                return true;

            if ((comp is AudioSource || typeName.Contains("AudioSource")) && profile.MaxAudioSources <= 0)
                return true;

            if (comp is Rigidbody && profile.MaxRigidbodies <= 0)
                return true;

            if (comp is ParticleSystem && profile.MaxParticleSystems <= 0)
                return true;

            if (comp is TrailRenderer && profile.MaxTrailRenderers <= 0)
                return true;

            if (comp is LineRenderer && profile.MaxLineRenderers <= 0)
                return true;

            // Physics Colliders (exclude PhysBoneColliders)
            if (comp is Collider && !typeName.Contains("VRCPhysBoneCollider") && profile.MaxPhysicsColliders <= 0)
                return true;

            return false;
        }

        /// <summary>
        /// Deletes unused GameObjects that have no components, are not referenced by any bone or component,
        /// and re-parents any remaining children to the parent transform.
        /// </summary>
        public static int DeleteUnusedGameObjects(GameObject avatarRoot, Action<string> progressCallback = null)
        {
            if (avatarRoot == null) return 0;

            progressCallback?.Invoke("Searching for unused GameObjects...");

            // Collect all referenced transforms (bones in SMRs, PhysBone targets, root)
            HashSet<Transform> referencedTransforms = new HashSet<Transform> { avatarRoot.transform };

            SkinnedMeshRenderer[] smrs = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                if (smr == null) continue;
                if (smr.rootBone != null) referencedTransforms.Add(smr.rootBone);
                if (smr.bones != null)
                {
                    foreach (var b in smr.bones)
                        if (b != null) referencedTransforms.Add(b);
                }
            }

            Component[] allComps = avatarRoot.GetComponentsInChildren<Component>(true);
            foreach (var c in allComps)
            {
                if (c != null && c.transform != null)
                    referencedTransforms.Add(c.transform);
            }

            HashSet<string> animatedPaths = CollectAnimatedPaths(avatarRoot);

            List<Transform> allTransforms = avatarRoot.GetComponentsInChildren<Transform>(true)
                .Where(t => t != null && t.gameObject != avatarRoot)
                .OrderByDescending(t => t.GetHierarchyDepth())
                .ToList();

            int deletedCount = 0;

            foreach (Transform t in allTransforms)
            {
                if (t == null || referencedTransforms.Contains(t)) continue;

                // Check if GameObject has components other than Transform
                Component[] comps = t.GetComponents<Component>();
                if (comps.Length > 1) continue; // Has active/disabled components

                // Re-parenting would change every descendant's hierarchy path, and animation curves
                // address objects by path string — the curves would silently stop resolving. Deleting is
                // only safe once the transform is a leaf, which the deepest-first ordering above achieves
                // for genuinely empty chains.
                if (t.childCount > 0)
                {
                    Debug.Log($"[AvatarComponentRemover] Keeping unused GameObject '{GetGameObjectPath(t.gameObject)}': it still has {t.childCount} child(ren), and re-parenting them would break animation paths.");
                    continue;
                }

                // An object an animation curve targets is in use even with no components — a curve may
                // toggle its active state.
                if (animatedPaths.Contains(AnimationUtility.CalculateTransformPath(t, avatarRoot.transform)))
                {
                    Debug.Log($"[AvatarComponentRemover] Keeping unused GameObject '{GetGameObjectPath(t.gameObject)}': it is targeted by an animation curve.");
                    continue;
                }

                Debug.Log($"[AvatarComponentRemover] Deleting unused GameObject '{GetGameObjectPath(t.gameObject)}'");
                Undo.DestroyObjectImmediate(t.gameObject);
                deletedCount++;
            }

            if (deletedCount > 0)
                Debug.Log($"[AvatarComponentRemover] Deleted {deletedCount} unused GameObject(s).");

            return deletedCount;
        }

        /// <summary>
        /// Every hierarchy path referenced by an animation curve on this avatar. Curves address objects by
        /// path string, so anything listed here must keep both its name and its position in the hierarchy.
        /// </summary>
        private static HashSet<string> CollectAnimatedPaths(GameObject avatarRoot)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);

            foreach (Animator anim in avatarRoot.GetComponentsInChildren<Animator>(true))
            {
                if (anim == null || anim.runtimeAnimatorController == null) continue;

                AnimationClip[] clips = anim.runtimeAnimatorController.animationClips;
                if (clips == null) continue;

                foreach (AnimationClip clip in clips)
                {
                    if (clip == null) continue;

                    foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                        paths.Add(b.path);
                    foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                        paths.Add(b.path);
                }
            }

            return paths;
        }
    }
}
