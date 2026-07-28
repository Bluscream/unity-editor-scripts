using Bluscream.VRC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using TextureCompressionEditor = global::Bluscream.TextureCompressor.TextureCompressionEditor;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Core conversion pipeline orchestrating the avatar optimization and platform patching process
    /// </summary>
    public static class VRCAvatarOptimizerCore
    {
        public class ConversionConfig
        {
            public TargetPlatform Platform = TargetPlatform.Android;
            public AvatarPerformanceRank TargetRank = AvatarPerformanceRank.Medium;
            public AssetPlacementLocation PlacementLocation = AssetPlacementLocation.SeparateFolder;
            public PhysBonePruningStrategy PruningStrategy = PhysBonePruningStrategy.DeepestFirst;
            public bool DuplicateAvatar = true;
            public bool AddPlatformSuffixes = true;
            public string AvatarSuffix = null; // null = use profile.PlatformSuffix
            // Opt-in: the VRC SDK panel's Auto Fix already removes illegal components, converts
            // DynamicBones -> PhysBones, and converts Unity constraints -> VRC constraints (conversion
            // preserves behavior where our pass just deletes), so the destructive local pass is off by default.
            public bool RemoveIncompatibleComponents = false;
            // Skip the Step 8.5 dry-run bundle builds entirely and rely on Step 5's fast-math size
            // estimate only. Faster, but no verified compressed bundle size in the summary.
            public bool SkipDryRunBundleBuild = false;
            // How many times Step 8.5 may tighten the texture budget and rebuild when the measured
            // bundle exceeds the platform cap. Each attempt costs one full SDK build. 0 = never retry.
            public int MaxSizeConvergenceAttempts = 3;
            public bool ReplaceShaders = true;
            public bool OptimizeTextures = true;
            public int MaxTextureResolution = 2048; // 4096, 2048, 1024, 512, 256, 128
            public int CrunchCompressionQuality = 75; // 0 = No Crunching (ASTC raw), 100 = Max Crunch (lowest file size)
            public float UncompressedAvatarHeadroomMB = 4.0f; // Headroom in MB reserved for mesh & animation payload from 40.0 MB limit
            public float CompressedAvatarHeadroomMB = 1.5f;   // Headroom in MB reserved for compressed avatar AssetBundle from 10.0 MB limit
            public int CrunchStepPercent = 10;                 // Step size for Crunch quality ladder in Step 5 estimator and Step 8.5 real build verification (1-50)
            public bool DecimateMeshes = true;
            public bool RemapAnimationsAndVRCFury = true;
            public bool DeletePlacementLocationBeforeConversion = false;
            public bool DeleteExistingTargetGameObjects = false;
            public bool ClearEditorLogBeforeConversion = false;
        }

        public static ConversionSummary ConvertAvatar(
            GameObject avatarRoot, 
            ConversionConfig config, 
            Action<string, float> progressCallback = null)
        {
            ConversionSummary summary = new ConversionSummary();

            if (config.ClearEditorLogBeforeConversion)
            {
                ClearEditorLog();
            }

            if (avatarRoot == null)
            {
                Debug.LogError("[VRCAvatarOptimizerCore] ConvertAvatar called with null avatarRoot!");
                summary.AddError("Avatar root is null");
                return summary;
            }

            // Group all scene operations into a single Undo step (asset writes cannot be undone)
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName($"Avatar Optimization ({avatarRoot.name})");
            int undoGroup = Undo.GetCurrentGroup();

            Debug.Log($"[VRCAvatarOptimizerCore] ===== Starting Avatar Conversion for '{avatarRoot.name}' =====");
            Debug.Log($"[VRCAvatarOptimizerCore] Config: Platform={config.Platform}, Rank={config.TargetRank}, Duplicate={config.DuplicateAvatar}, ReplaceShaders={config.ReplaceShaders}, OptimizeTextures={config.OptimizeTextures}, PrunePhysBones={config.PruningStrategy}, DecimateMeshes={config.DecimateMeshes}, RemoveIncompatible={config.RemoveIncompatibleComponents}, Animations={config.RemapAnimationsAndVRCFury}, DeletePlacementFolder={config.DeletePlacementLocationBeforeConversion}, DeleteExistingTargetName={config.DeleteExistingTargetGameObjects}");

            // Step 0: Always switch active build target to match target platform as mandatory first step
            progressCallback?.Invoke($"Ensuring active build target is set to {config.Platform}...", 0.01f);
            SwitchBuildTargetIfNeeded(config.Platform);

            GameObject targetAvatar = avatarRoot;
            PlatformProfile profile = PlatformProfile.GetProfile(config.Platform, config.TargetRank);
            summary.Profile = profile;

            // The name the converted avatar will have — asset output paths are derived from it,
            // so it must be known before we can delete the placement folder.
            string expectedTargetName = config.DuplicateAvatar ? GetTargetAvatarName(avatarRoot.name, config, profile) : avatarRoot.name;

            // Delete placement location before starting if configured
            if (config.DeletePlacementLocationBeforeConversion)
            {
                string folderPath = GetPlacementFolder(expectedTargetName, config.PlacementLocation);
                if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                {
                    Debug.Log($"[VRCAvatarOptimizerCore] Deleting asset placement location before starting: '{folderPath}'");
                    AssetDatabase.DeleteAsset(folderPath);
                    AssetDatabase.Refresh();
                }
            }

            summary.InitialStats = AvatarSDKEvaluator.EvaluateAvatar(avatarRoot);
            Debug.Log($"[VRCAvatarOptimizerCore] Initial stats — Tris: {summary.InitialStats.TriangleCount:N0}, TexMem: {summary.InitialStats.TotalTextureMemoryBytes / (1024.0 * 1024.0):F1} MB, MatSlots: {summary.InitialStats.MaterialSlotCount}, PhysBones: {summary.InitialStats.PhysBoneComponentCount}, Colliders: {summary.InitialStats.PhysBoneColliderCount}, CollisionChecks: {summary.InitialStats.PhysBoneCollisionCheckCount}");

            Debug.Log($"[VRCAvatarOptimizerCore] Profile limits — Platform: {profile.Platform}, Rank: {profile.Rank}, Tris: {(profile.MaxTriangles == int.MaxValue ? "Unlimited" : profile.MaxTriangles.ToString("N0"))}, TexMem: {profile.MaxTextureMemoryBytes / (1024.0 * 1024.0):F0} MB, PhysBones: {profile.MaxPhysBoneComponents}, Colliders: {profile.MaxPhysBoneColliders}, CollisionChecks: {profile.MaxPhysBoneCollisionChecks}");

            try
            {
                // Step 1: Duplicate Avatar GameObject & Manage Platform Suffixes / Active State
                if (config.DuplicateAvatar)
                {
                    progressCallback?.Invoke("Duplicating avatar GameObject...", 0.05f);
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 1] Duplicating avatar '{avatarRoot.name}' for target platform '{config.Platform}'...");
                    string cleanName = StripPlatformSuffix(avatarRoot.name);

                    // Delete existing GameObjects with the target name if requested
                    if (config.DeleteExistingTargetGameObjects)
                    {
                        var existingObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                            .Where(go => go != null && go != avatarRoot && go.name == expectedTargetName)
                            .ToList();

                        foreach (var existing in existingObjects)
                        {
                            Debug.Log($"[VRCAvatarOptimizerCore] [Step 1] Deleting existing target GameObject '{existing.name}' before conversion...");
                            Undo.DestroyObjectImmediate(existing);
                        }
                    }

                    if (config.AddPlatformSuffixes)
                    {
                        Undo.RecordObject(avatarRoot, "Rename Original Avatar");
                        avatarRoot.name = cleanName + (config.Platform == TargetPlatform.PC ? " (Original)" : " (PC)");
                    }

                    Undo.RecordObject(avatarRoot, "Disable Original Avatar");
                    avatarRoot.SetActive(false);

                    targetAvatar = UnityEngine.Object.Instantiate(avatarRoot, avatarRoot.transform.parent);
                    targetAvatar.name = expectedTargetName;
                    targetAvatar.SetActive(true);

                    Undo.RegisterCreatedObjectUndo(targetAvatar, "Create Avatar Clone");
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 1] Created clone: '{targetAvatar.name}'");
                    summary.AddSuccess($"Created Avatar clone: {targetAvatar.name}", targetAvatar);
                }
                else
                {
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 1] Skipped duplication — editing '{targetAvatar.name}' in-place.");
                }

                // Step 2: Remove Platform-Incompatible Components
                if (config.RemoveIncompatibleComponents)
                {
                    progressCallback?.Invoke("Removing incompatible components...", 0.15f);
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 2] Removing incompatible components from '{targetAvatar.name}'...");
                    var removedComps = AvatarComponentRemover.RemoveIncompatibleComponents(
                        targetAvatar, 
                        profile,
                        (msg) => progressCallback?.Invoke(msg, 0.15f)
                    );
                    summary.componentsRemoved = removedComps.Count;
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 2] Removed {removedComps.Count} incompatible components.");
                    if (removedComps.Count > 0)
                    {
                        var grouped = new Dictionary<string, int>();
                        foreach (var rc in removedComps) {
                            string t = rc.componentType?.Split('.')?.LastOrDefault() ?? rc.componentType;
                            grouped[t] = grouped.TryGetValue(t, out int v) ? v + 1 : 1;
                        }
                        foreach (var kv in grouped)
                            Debug.Log($"[VRCAvatarOptimizerCore] [Step 2]   {kv.Value}x {kv.Key}");
                    }
                }
                else
                {
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 2] Skipped incompatible component removal (disabled in config).");
                }

                // Step 3: Duplicate Materials & Remap Shaders
                Dictionary<Material, Material> materialMap = new Dictionary<Material, Material>();
                if (config.ReplaceShaders)
                {
                    progressCallback?.Invoke("Duplicating materials and replacing shaders...", 0.30f);
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 3] Duplicating materials and remapping shaders on '{targetAvatar.name}'...");
                    DuplicateAndReplaceMaterials(targetAvatar, config, profile, summary, materialMap, (msg, prog) => progressCallback?.Invoke(msg, 0.30f + prog * 0.20f));
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 3] Materials processed: {materialMap.Count} unique. Replaced: {summary.materialsReplaced}, Skipped: {summary.materialsSkipped}, Failed: {summary.materialsFailed}.");
                }
                else
                {
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 3] Skipped material/shader replacement (disabled in config).");
                }

                // Step 4: Remap AnimatorControllers, AnimationClips, and VRCFury Components
                if (config.RemapAnimationsAndVRCFury && materialMap.Count > 0)
                {
                    progressCallback?.Invoke("Rewriting Animator, Clips, Material Swaps, and VRCFury...", 0.55f);
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 4] Rewriting animations/VRCFury for '{targetAvatar.name}' with {materialMap.Count} material remaps...");
                    AvatarAnimationRewriter.ProcessAvatarAnimationsAndVRCFury(
                        targetAvatar,
                        materialMap,
                        GetPlacementFolder(targetAvatar.name, config.PlacementLocation),
                        (msg) => progressCallback?.Invoke(msg, 0.55f)
                    );
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 4] Animation rewrite complete.");
                }

                // Step 5: Fast Texture Optimization & Memory Budget Estimate
                if (config.OptimizeTextures)
                {
                    progressCallback?.Invoke("Optimizing texture memory budget...", 0.70f);
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 5] Optimizing textures — VRAM budget: {profile.MaxTextureMemoryBytes / (1024.0 * 1024.0):F0} MB");
                    int texCount = TextureCompressionEditor.OptimizeForTextureMemoryBudget(
                        targetAvatar, 
                        profile.MaxTextureMemoryBytes, 
                        (msg) => progressCallback?.Invoke(msg, 0.70f),
                        config.MaxTextureResolution,
                        config.CrunchCompressionQuality,
                        config.UncompressedAvatarHeadroomMB,
                        config.CompressedAvatarHeadroomMB,
                        config.CrunchStepPercent
                    );
                    summary.texturesOptimized = texCount;
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 5] Initial texture optimization complete: {texCount} texture(s) reimported.");
                }

                // Step 6: PhysBone Budget Pruner
                if (config.PruningStrategy != PhysBonePruningStrategy.Disabled)
                {
                    progressCallback?.Invoke("Pruning PhysBones to hit target rank limits...", 0.85f);
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 6] Pruning PhysBones — strategy={config.PruningStrategy}, target: ≤{profile.MaxPhysBoneComponents} PBs, ≤{profile.MaxPhysBoneColliders} colliders, ≤{profile.MaxPhysBoneCollisionChecks} collision checks.");
                    int pruned = AvatarPhysBonePruner.PrunePhysBones(targetAvatar, profile, config.PruningStrategy, (msg) => progressCallback?.Invoke(msg, 0.85f));
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 6] PhysBone pruning complete: {pruned} component(s)/collider(s) removed.");
                    summary.AddSuccess($"Pruned {pruned} PhysBone components/colliders to comply with rank '{profile.Rank}'.");
                }

                // Step 7: Mesh Decimation to hit Target Poly Count Limit
                if (config.DecimateMeshes)
                {
                    progressCallback?.Invoke("Decimating avatar meshes to target triangle budget...", 0.92f);
                    string triLimitStr = profile.MaxTriangles == int.MaxValue ? "Unlimited" : profile.MaxTriangles.ToString("N0");
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 7] Decimating meshes — target triangle limit: {triLimitStr} (current: {summary.InitialStats.TriangleCount:N0}).");
                    int finalTris = Bluscream.MobileDecimater.Editor.MobileDecimationProcessor.DecimateAvatarMeshesToTargetTris(
                        targetAvatar, 
                        profile.MaxTriangles, 
                        (msg) => progressCallback?.Invoke(msg, 0.92f)
                    );
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 7] Decimation complete. Final triangle count: {finalTris:N0} (target was {triLimitStr}).");
                    summary.AddSuccess($"Mesh decimation complete. Final triangle count: {finalTris:N0} (Target: {triLimitStr}).");
                }

                // Step 7.5: Material Slot Consolidation & Mesh Count Optimization
                progressCallback?.Invoke("Consolidating material slots and mesh count...", 0.94f);
                string meshAssetDir = GetPlacementFolder(targetAvatar.name, config.PlacementLocation);
                AvatarMaterialSlotOptimizer.OptimizeMaterialSlots(targetAvatar, profile.MaxMaterialSlots, meshAssetDir, (msg) => progressCallback?.Invoke(msg, 0.94f));
                AvatarMeshCountOptimizer.OptimizeMeshCount(targetAvatar, profile.MaxSkinnedMeshes, profile.MaxMeshRenderers, meshAssetDir, (msg) => progressCallback?.Invoke(msg, 0.94f));

                // Step 8: Platform-Specific Profile Conversions & Rule Validation
                progressCallback?.Invoke("Executing platform-specific profile conversions & validation...", 0.95f);
                profile.ExecutePlatformConversions(targetAvatar, (msg) => progressCallback?.Invoke(msg, 0.95f));
                profile.ValidatePlatformRules(targetAvatar, summary);

                // Step 8.5: Fast Math Iterative AssetBundle Verification & Smart Quality Ladder
                if (config.SkipDryRunBundleBuild)
                {
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 8.5] Skipped dry-run bundle build (disabled in config) — using Step 5's texture memory estimate only.");
                    summary.AddWarning("Dry-run bundle build skipped — compressed avatar size was not verified (Step 5 estimate only).");
                }
                else
                {
                progressCallback?.Invoke("Verifying compressed AssetBundle size...", 0.98f);
                Debug.Log($"[VRCAvatarOptimizerCore] [Step 8.5] Verifying AssetBundle size for '{targetAvatar.name}'...");

                // Ensure active build target matches target platform profile before dry-run bundle build
                SwitchBuildTargetIfNeeded(config.Platform);

                // ALWAYS temporarily strip SDK-incompatible components around the dry-run builds (unless
                // they were already permanently removed in Step 2): the measured size must match what an
                // SDK-auto-fixed upload would weigh — audio clips, particle textures etc. referenced by
                // doomed components would otherwise inflate the bundle. The removals are recorded in their
                // own Undo group and reverted right after the builds, leaving the components in place for
                // the SDK panel's Auto Fix (which converts instead of deleting).
                bool tempRemove = !config.RemoveIncompatibleComponents;
                int tempRemoveUndoGroup = -1;
                if (tempRemove)
                {
                    Undo.IncrementCurrentGroup();
                    tempRemoveUndoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Temp Component Removal (size check)");
                    progressCallback?.Invoke("Temporarily removing incompatible components for size measurement...", 0.975f);
                    var tempRemoved = AvatarComponentRemover.RemoveIncompatibleComponents(targetAvatar, profile, (msg) => progressCallback?.Invoke(msg, 0.975f));
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 8.5] Temporarily removed {tempRemoved.Count} incompatible component(s) for size measurement (will be restored after the dry-run builds).");
                }

                long bundleSizeBytes = -1;
                string bundlePath = null;
                long maxBundleBytes = profile.MaxAssetBundleSizeBytes; // also used by the reporting block below
                try // ensure temp-removed components are ALWAYS restored, even on exception/cancel
                {
                try
                {
                    bundleSizeBytes = AvatarSDKEvaluator.BuildAvatarAssetBundle(targetAvatar, out bundlePath, (msg) => progressCallback?.Invoke(msg, 0.98f));
                }
                catch (InvalidOperationException ex)
                {
                    Debug.LogError($"[VRCAvatarOptimizerCore] [Step 8.5] ⚠️ CRITICAL: Failed to obtain compressed AssetBundle size — {ex.Message}");
                    summary.AddError($"⚠️ CRITICAL: Could not verify compressed bundle size. SDK dry-run was suppressed or failed. Check console for details.");
                }

                AvatarSDKEvaluator.AvatarStats currentStats = AvatarSDKEvaluator.EvaluateAvatar(targetAvatar);
                long headroomBytes = (long)(config.UncompressedAvatarHeadroomMB * 1024 * 1024);
                long maxUncompressedBytes = Math.Max(1024 * 1024L /* 1 MB */, profile.MaxTextureMemoryBytes - headroomBytes);

                // Only check bundle size if we successfully got one (bundleSizeBytes > 0)
                bool bundleExceeds = (bundleSizeBytes > 0 && maxBundleBytes != long.MaxValue && bundleSizeBytes > maxBundleBytes);
                // Apply a 5% tolerance to the uncompressed check ONLY when the user's headroom is itself
                // at least 5% of the hard platform limit — guaranteeing the tolerance can't push us past
                // the real ceiling. If headroom is too tight (e.g. 1 MB / 40 MB = 2.5%), no tolerance is added.
                double headroomFraction = profile.MaxTextureMemoryBytes > 0 ? (double)headroomBytes / profile.MaxTextureMemoryBytes : 0.0;
                const double toleranceThreshold = 0.05; // 5%
                double uncompressedEffectiveLimit = headroomFraction >= toleranceThreshold
                    ? maxUncompressedBytes * (1.0 + toleranceThreshold)  // safe to add 5% breathing room
                    : maxUncompressedBytes;                                // headroom too tight, use exact budget
                bool uncompressedExceeds = (currentStats.TotalTextureMemoryBytes > (long)uncompressedEffectiveLimit);


                // Convergence loop: the Step 5 estimator only models TEXTURE memory, but the real bundle
                // also carries meshes, animation clips, and controllers. When the measured bundle exceeds
                // the platform cap we feed the measured overshoot back as a TIGHTER texture budget and
                // rebuild, repeating until it fits, the texture ladder bottoms out, or we run out of
                // attempts. (Re-running with the original arguments would be a deterministic no-op.)
                long textureBudgetBytes = profile.MaxTextureMemoryBytes;
                for (int attempt = 1; attempt <= Math.Max(0, config.MaxSizeConvergenceAttempts) && (bundleExceeds || uncompressedExceeds); attempt++)
                {
                    double currentBundleMB = bundleSizeBytes > 0 ? bundleSizeBytes / (1024.0 * 1024.0) : -1;
                    double currentUncompressedMB = currentStats.TotalTextureMemoryBytes / (1024.0 * 1024.0);
                    double targetUncompressedMB = maxUncompressedBytes / (1024.0 * 1024.0);

                    long previousBudget = textureBudgetBytes;
                    if (bundleExceeds)
                    {
                        // Non-texture payload is (measured bundle - texture contribution) and cannot be
                        // compressed by us, so scaling the texture budget by limit/measured under-corrects
                        // on purpose: subtract the excess directly, then clamp to a sane floor.
                        long excessBytes = bundleSizeBytes - maxBundleBytes;
                        // Bundle bytes are compressed; textures must give up more raw bytes than the
                        // compressed excess. Use the observed compression ratio to scale the correction up.
                        double compressionRatio = bundleSizeBytes > 0 && currentStats.TotalTextureMemoryBytes > 0
                            ? Math.Max(0.05, (double)bundleSizeBytes / currentStats.TotalTextureMemoryBytes)
                            : 1.0;
                        long rawReduction = (long)(excessBytes / compressionRatio);
                        // Always make meaningful progress even if the math suggests a tiny step
                        long minStep = (long)(textureBudgetBytes * 0.10);
                        textureBudgetBytes -= Math.Max(rawReduction, minStep);
                    }
                    if (uncompressedExceeds)
                    {
                        textureBudgetBytes = Math.Min(textureBudgetBytes, (long)(maxUncompressedBytes * 0.95));
                    }
                    textureBudgetBytes = Math.Max(2 * 1024 * 1024L /* 2 MB floor */, textureBudgetBytes);

                    Debug.LogWarning($"[VRCAvatarOptimizerCore] [Step 8.5] Attempt {attempt}/{config.MaxSizeConvergenceAttempts}: avatar exceeds budget (Compressed: {(currentBundleMB >= 0 ? currentBundleMB.ToString("F2") + " MB" : "unknown")} / {maxBundleBytes / (1024.0 * 1024.0):F2} MB, Uncompressed TexMem: {currentUncompressedMB:F2} MB > {targetUncompressedMB:F1} MB). Tightening texture budget {previousBudget / (1024.0 * 1024.0):F1} MB → {textureBudgetBytes / (1024.0 * 1024.0):F1} MB and rebuilding...");
                    progressCallback?.Invoke($"Size convergence attempt {attempt}/{config.MaxSizeConvergenceAttempts} (target texture budget {textureBudgetBytes / (1024.0 * 1024.0):F1} MB)...", 0.98f);

                    if (previousBudget == textureBudgetBytes)
                    {
                        Debug.LogWarning($"[VRCAvatarOptimizerCore] [Step 8.5] Texture budget hit its floor — further compression cannot shrink the bundle. Stopping convergence.");
                        break;
                    }

                    TextureCompressionEditor.OptimizeForTextureMemoryBudget(
                        targetAvatar,
                        textureBudgetBytes,
                        (msg) => progressCallback?.Invoke(msg, 0.98f),
                        config.MaxTextureResolution,
                        config.CrunchCompressionQuality,
                        config.UncompressedAvatarHeadroomMB,
                        config.CompressedAvatarHeadroomMB,
                        config.CrunchStepPercent
                    );

                    progressCallback?.Invoke($"Rebuilding dry-run AssetBundle (attempt {attempt}/{config.MaxSizeConvergenceAttempts})...", 0.99f);
                    long previousBundleSize = bundleSizeBytes;
                    try
                    {
                        bundleSizeBytes = AvatarSDKEvaluator.BuildAvatarAssetBundle(targetAvatar, out bundlePath, (msg) => progressCallback?.Invoke(msg, 0.99f));
                    }
                    catch (InvalidOperationException ex)
                    {
                        Debug.LogError($"[VRCAvatarOptimizerCore] [Step 8.5] ⚠️ CRITICAL: AssetBundle verification failed on attempt {attempt} — {ex.Message}");
                        summary.AddError($"⚠️ CRITICAL: Bundle size verification failed on attempt {attempt}. See console for details.");
                        break;
                    }

                    currentStats = AvatarSDKEvaluator.EvaluateAvatar(targetAvatar);
                    bundleExceeds = (bundleSizeBytes > 0 && maxBundleBytes != long.MaxValue && bundleSizeBytes > maxBundleBytes);
                    uncompressedExceeds = (currentStats.TotalTextureMemoryBytes > (long)uncompressedEffectiveLimit);

                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 8.5] Attempt {attempt} result: {bundleSizeBytes / (1024.0 * 1024.0):F2} MB compressed, {currentStats.TotalTextureMemoryBytes / (1024.0 * 1024.0):F2} MB texture VRAM.{(bundleExceeds || uncompressedExceeds ? "" : " ✓ Within budget.")}");

                    // Guard against a pass that cannot shrink the bundle any further
                    if (bundleSizeBytes > 0 && previousBundleSize > 0 && bundleSizeBytes >= previousBundleSize)
                    {
                        Debug.LogWarning($"[VRCAvatarOptimizerCore] [Step 8.5] Bundle did not shrink ({previousBundleSize / (1024.0 * 1024.0):F2} MB → {bundleSizeBytes / (1024.0 * 1024.0):F2} MB) — remaining size is non-texture payload (meshes/animations/controllers). Stopping convergence.");
                        summary.AddWarning("Texture compression can no longer shrink the avatar — the remaining bundle size is mesh/animation/controller payload. Reduce meshes, blendshapes, or animator content.");
                        break;
                    }
                }
                }
                finally
                {
                    if (tempRemove)
                    {
                        Undo.RevertAllDownToGroup(tempRemoveUndoGroup);
                        Debug.Log("[VRCAvatarOptimizerCore] [Step 8.5] Restored temporarily removed components — use the SDK panel's Auto Fix to convert/remove them at upload time.");
                        summary.AddSuccess("Measured bundle size with incompatible components temporarily removed; components were restored afterwards (SDK Auto Fix will handle them at upload).");
                    }
                }

                summary.CompressedAvatarSizeBytes = bundleSizeBytes;
                if (bundleSizeBytes > 0)
                {
                    double bundleMB = bundleSizeBytes / (1024.0 * 1024.0);
                    if (maxBundleBytes != long.MaxValue && bundleSizeBytes > maxBundleBytes)
                    {
                        double limitMB = maxBundleBytes / (1024.0 * 1024.0);
                        Debug.LogWarning($"[VRCAvatarOptimizerCore] [Step 8.5] ⚠️ WARNING: Built compressed avatar size is {bundleMB:F2} MB (exceeds {profile.Platform} limit of {limitMB:F2} MB!).");
                        summary.AddError($"Compressed avatar size ({bundleMB:F2} MB) exceeds {profile.Platform} limit ({limitMB:F2} MB)!");
                    }
                    else
                    {
                        Debug.Log($"[VRCAvatarOptimizerCore] [Step 8.5] ✓ Verified compressed avatar size: {bundleMB:F2} MB. Bundle file: {bundlePath}");
                        summary.AddSuccess($"Verified compressed avatar size: {bundleMB:F2} MB.");
                    }
                }
                } // end Step 8.5 (dry-run bundle verification)

                AvatarSDKEvaluator.AvatarStats stats = AvatarSDKEvaluator.EvaluateAvatar(targetAvatar);
                summary.FinalStats = stats;

                Debug.Log($"<color=cyan><b>================================================================================</b></color>");
                Debug.Log($"<color=cyan><b>[VRCAvatarOptimizerCore] BEFORE Conversion Report for '{avatarRoot.name}':</b></color>");
                AvatarSDKEvaluator.PrintSDKAlertsToConsole(avatarRoot, summary.InitialStats);

                Debug.Log($"<color=cyan><b>================================================================================</b></color>");
                Debug.Log($"<color=cyan><b>[VRCAvatarOptimizerCore] AFTER Conversion Report for '{targetAvatar.name}':</b></color>");
                AvatarSDKEvaluator.PrintSDKAlertsToConsole(targetAvatar, stats);

                summary.PrintConsoleSummary(targetAvatar.name, profile);

                string bundleStr = summary.CompressedAvatarSizeBytes > 0 ? $" ({summary.CompressedAvatarSizeBytes / (1024.0 * 1024.0):F2} MB Compressed Avatar)" : "";
                Debug.Log($"[VRCAvatarOptimizerCore] ===== Conversion Complete for '{targetAvatar.name}'{bundleStr} — {summary.materialsReplaced} mats replaced, {summary.texturesOptimized} textures compressed, {summary.componentsRemoved} components removed =====");
                progressCallback?.Invoke("Conversion completed successfully!", 1.0f);

            }
            catch (OperationCanceledException)
            {
                // User canceled via the progress bar — let the caller (window) handle it, don't report success.
                throw;
            }
            catch (Exception e)
            {
                summary.AddError($"Conversion failed: {e.Message}\n{e.StackTrace}");
                Debug.LogError($"[VRCAvatarOptimizerCore] Conversion FAILED for '{avatarRoot.name}': {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                // One Ctrl+Z reverts all scene changes of this conversion
                Undo.CollapseUndoOperations(undoGroup);
            }

            return summary;
        }

        /// <summary>
        /// Strips any trailing platform/rank suffix, e.g. " (Android) [Very Poor]" or " (PC) [Excellent]".
        /// </summary>
        public static string StripPlatformSuffix(string avatarName)
        {
            return Regex.Replace(
                avatarName,
                @"\s*\((?:PC|Android|iOS|Quest|Original|Optimized)\)(?:\s*\[[^\]]*\])?\s*$",
                ""
            ).TrimEnd();
        }

        /// <summary>
        /// Computes the name the converted avatar duplicate will get for the given config/profile.
        /// </summary>
        public static string GetTargetAvatarName(string sourceName, ConversionConfig config, PlatformProfile profile)
        {
            string cleanName = StripPlatformSuffix(sourceName);
            if (config.AddPlatformSuffixes)
                return cleanName + profile.PlatformSuffix;
            // Without platform suffixes, only append an explicitly configured custom suffix.
            return cleanName + (config.AvatarSuffix ?? "");
        }

        /// <summary>
        /// Asset output folder for a converted avatar. Expects the FINAL (target) avatar name —
        /// this must match the folder that DuplicateMaterial and the animation rewriter write into.
        /// </summary>
        public static string GetPlacementFolder(string targetAvatarName, AssetPlacementLocation location)
        {
            if (location == AssetPlacementLocation.SeparateFolder)
            {
                return "Assets/_AVATAROPTIMIZER/" + targetAvatarName;
            }
            return null;
        }

        public static void SwitchBuildTargetIfNeeded(TargetPlatform targetPlatform)
        {
            BuildTargetGroup expectedGroup;
            BuildTarget expectedTarget;
            switch (targetPlatform)
            {
                case TargetPlatform.Android:
                    expectedGroup = BuildTargetGroup.Android;
                    expectedTarget = BuildTarget.Android;
                    break;
                case TargetPlatform.iOS:
                    expectedGroup = BuildTargetGroup.iOS;
                    expectedTarget = BuildTarget.iOS;
                    break;
                default:
                    expectedGroup = BuildTargetGroup.Standalone;
                    expectedTarget = BuildTarget.StandaloneWindows64;
                    break;
            }

            if (EditorUserBuildSettings.activeBuildTarget != expectedTarget)
            {
                Debug.Log($"[VRCAvatarOptimizerCore] Switching active build target to {expectedTarget} ({expectedGroup})...");
                bool success = EditorUserBuildSettings.SwitchActiveBuildTarget(expectedGroup, expectedTarget);
                if (success)
                {
                    Debug.Log($"[VRCAvatarOptimizerCore] Successfully switched build target to {expectedTarget}.");
                }
                else
                {
                    Debug.LogWarning($"[VRCAvatarOptimizerCore] Build target switch to {expectedTarget} scheduled / pending.");
                }
            }
        }

        public static void ClearEditorLog()
        {
            try
            {
                string logPath;
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log");
                }
                else if (Application.platform == RuntimePlatform.OSXEditor)
                {
                    logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Logs/Unity/Editor.log");
                }
                else // Linux
                {
                    logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config/unity3d/Editor.log");
                }

                if (File.Exists(logPath))
                {
                    using (FileStream fs = new FileStream(logPath, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite))
                    {
                        fs.SetLength(0);
                    }
                    Debug.Log("[VRCAvatarOptimizerCore] Unity Editor.log cleared successfully before conversion.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VRCAvatarOptimizerCore] Could not clear Editor.log: {ex.Message}");
            }
        }

        private static void DuplicateAndReplaceMaterials(
            GameObject avatarRoot, 
            ConversionConfig config, 
            PlatformProfile profile,
            ConversionSummary summary, 
            Dictionary<Material, Material> materialMap, 
            Action<string, float> progressCallback)
        {
            Renderer[] renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            List<(Renderer renderer, int materialIndex, Material originalMat)> matList = new List<(Renderer, int, Material)>();

            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                Material[] sharedMats = r.sharedMaterials;
                for (int i = 0; i < sharedMats.Length; i++)
                {
                    if (sharedMats[i] != null)
                    {
                        matList.Add((r, i, sharedMats[i]));
                    }
                }
            }

            int total = matList.Count;
            for (int i = 0; i < matList.Count; i++)
            {
                var entry = matList[i];
                Material srcMat = entry.originalMat;
                progressCallback?.Invoke($"Processing material ({i + 1}/{total}): {srcMat.name}", (float)i / total);

                if (!materialMap.TryGetValue(srcMat, out Material questMat))
                {
                    questMat = DuplicateMaterial(srcMat, config.PlacementLocation == AssetPlacementLocation.SameFolderAsOriginal, avatarRoot.name, profile.PlatformSuffix);
                    if (questMat != null)
                    {
                        // ReplaceShaderOnMaterial may re-create the asset (Material Variant conversion),
                        // so store the material it returns — not the possibly-destroyed input reference.
                        questMat = ReplaceShaderOnMaterial(srcMat, questMat, summary);
                        materialMap[srcMat] = questMat;
                    }
                    else
                    {
                        questMat = srcMat;
                    }
                }

                Material[] mats = entry.renderer.sharedMaterials;
                mats[entry.materialIndex] = questMat;
                Undo.RecordObject(entry.renderer, "Assign Optimized Material");
                entry.renderer.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// Creates a plain (non-variant) copy of a material. new Material(src) copies the variant
        /// parent link in Unity 2022+, so variants must be flattened explicitly — otherwise any later
        /// shader assignment fails with "Trying to set shader on a Material Variant".
        /// </summary>
        private static Material CreateFlattenedCopy(Material src)
        {
            if (!src.isVariant) return new Material(src);

            Material flat = new Material(src.shader);
            flat.CopyPropertiesFromMaterial(src); // effective values, including those inherited from the parent
            flat.shaderKeywords = src.shaderKeywords;
            flat.renderQueue = src.renderQueue;
            flat.enableInstancing = src.enableInstancing;
            flat.globalIlluminationFlags = src.globalIlluminationFlags;
            flat.doubleSidedGI = src.doubleSidedGI;
            return flat;
        }

        private static Material DuplicateMaterial(Material srcMat, bool saveInSameFolder, string avatarName, string platformSuffix = " (Optimized)")
        {
            if (srcMat == null) return null;

            string srcPath = AssetDatabase.GetAssetPath(srcMat);
            bool isBuiltIn = string.IsNullOrEmpty(srcPath) || srcPath.Contains("unity_builtin_extra") || srcPath.StartsWith("Resources/");

            string filename = !string.IsNullOrEmpty(srcMat.name) ? srcMat.name : "Material";
            // Skip materials that already carry any known optimized suffix
            if (filename.EndsWith(" (Quest)") || filename.EndsWith(" (iOS)") || filename.EndsWith(" (Optimized)") || filename.EndsWith(platformSuffix))
            {
                Debug.Log($"[VRCAvatarOptimizerCore] Material '{srcMat.name}' already has optimized suffix — skipping duplicate.");
                return srcMat;
            }

            string dir = GetPlacementFolder(avatarName, AssetPlacementLocation.SeparateFolder);
            if (saveInSameFolder && !isBuiltIn)
            {
                string srcDir = Path.GetDirectoryName(srcPath);
                if (!string.IsNullOrEmpty(srcDir)) dir = srcDir;
            }
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string destPath = Path.Combine(dir, filename + platformSuffix + ".mat").Replace('\\', '/');
            if (File.Exists(destPath))
            {
                Material existingMat = AssetDatabase.LoadAssetAtPath<Material>(destPath);
                if (existingMat != null)
                {
                    // If the existing cached material on disk is a Material Variant, Unity will block questMat.shader = replacement.
                    // Replace the variant asset with a standard Material asset.
                    bool isVariant = existingMat.isVariant;
                    if (isVariant)
                    {
                        Debug.Log($"[VRCAvatarOptimizerCore] Existing material at '{destPath}' is a Material Variant — re-creating as a standard Material asset.");
                        AssetDatabase.DeleteAsset(destPath);
                        Material freshMat = CreateFlattenedCopy(srcMat);
                        freshMat.name = Path.GetFileNameWithoutExtension(destPath);
                        AssetDatabase.CreateAsset(freshMat, destPath);
                        return AssetDatabase.LoadAssetAtPath<Material>(destPath);
                    }
                    Debug.Log($"[VRCAvatarOptimizerCore] Material already exists, reusing: {destPath}");
                    return existingMat;
                }
            }

            if (isBuiltIn)
            {
                Debug.Log($"[VRCAvatarOptimizerCore] Duplicating built-in material '{srcMat.name}' → {destPath}");
                Material newMat = CreateFlattenedCopy(srcMat);
                AssetDatabase.CreateAsset(newMat, destPath);
                return newMat;
            }

            // Material Variants throw "Trying to set shader on a Material Variant" on shader assignment,
            // and copies (CopyAsset or new Material(src)) keep the variant parent link.
            // Create a flattened independent Material asset instead.
            Debug.Log($"[VRCAvatarOptimizerCore] Creating material copy of '{srcMat.name}' → '{destPath}'");
            Material duplicatedMat = CreateFlattenedCopy(srcMat);
            duplicatedMat.name = Path.GetFileNameWithoutExtension(destPath);
            AssetDatabase.CreateAsset(duplicatedMat, destPath);
            return AssetDatabase.LoadAssetAtPath<Material>(destPath);
        }

        /// <summary>
        /// Replaces the shader on <paramref name="questMat"/> with a mobile-compatible one.
        /// Returns the material that ends up holding the replacement — this may be a NEW asset
        /// if the input was a Material Variant that had to be re-created, so callers must use
        /// the returned reference.
        /// </summary>
        private static Material ReplaceShaderOnMaterial(Material srcMat, Material questMat, ConversionSummary summary)
        {
            if (questMat == null || questMat.shader == null) return questMat;
            string originalShaderName = srcMat != null && srcMat.shader != null ? srcMat.shader.name : questMat.shader.name;

            if (questMat.shader.name.StartsWith("VRChat/Mobile/", StringComparison.OrdinalIgnoreCase) && originalShaderName.StartsWith("VRChat/Mobile/", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[VRCAvatarOptimizerCore] Material '{questMat.name}' already uses mobile shader '{originalShaderName}' — skipping.");
                summary.materialsSkipped++;
                return questMat;
            }

            var replacement = ShaderMapping.FindReplacementShader(originalShaderName);
            if (replacement.Success && replacement.ReplacementShader != null)
            {
                Debug.Log($"[VRCAvatarOptimizerCore] Shader swap: '{originalShaderName}' → '{replacement.ReplacementShader.name}' on '{questMat.name}'");
                Undo.RegisterCompleteObjectUndo(questMat, "Replace Shader for Quest");

                // Unity throws an error/warning if questMat is a Material Variant when changing questMat.shader.
                // If it is a variant, convert it to a standard Material asset.
                if (questMat.isVariant)
                {
                    string assetPath = AssetDatabase.GetAssetPath(questMat);
                    Debug.Log($"[VRCAvatarOptimizerCore] Material '{questMat.name}' at '{assetPath}' is a Material Variant. Converting to a standard Material asset before shader replacement.");
                    Material nonVariantMat = CreateFlattenedCopy(questMat);
                    nonVariantMat.name = questMat.name;
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        AssetDatabase.DeleteAsset(assetPath);
                        AssetDatabase.CreateAsset(nonVariantMat, assetPath);
                        AssetDatabase.SaveAssets();
                        AssetDatabase.Refresh();
                        questMat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                    }
                    else
                    {
                        questMat = nonVariantMat;
                    }
                }

                Material tempMat = CreateFlattenedCopy(questMat);

                questMat.shader = replacement.ReplacementShader;

                var transfer = ShaderPropertyMapper.TransferProperties(tempMat, questMat, replacement.ReplacementShader);
                UnityEngine.Object.DestroyImmediate(tempMat);

                questMat.enableInstancing = true;
                EditorUtility.SetDirty(questMat);

                summary.materialsReplaced++;
                summary.AddSuccess($"Replaced shader: {originalShaderName} → {replacement.ReplacementShader.name} on {questMat.name}");
            }
            else
            {
                Debug.LogWarning($"[VRCAvatarOptimizerCore] No Quest shader mapping for '{originalShaderName}' on material '{questMat.name}'. Add an entry to ShaderMapping to fix this.");
                summary.materialsFailed++;
                summary.AddError($"Could not find Quest replacement for shader: {originalShaderName} on material {questMat.name}");
            }

            return questMat;
        }
    }
}
