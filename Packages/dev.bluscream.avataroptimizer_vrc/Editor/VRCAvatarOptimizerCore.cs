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
            // Textures are fully automatic: resolution, format, crunch and budgets are all derived from
            // the target profile's caps and refined against real measured builds. See TextureAutoTuning.
            public bool OptimizeTextures = true;
            public bool DecimateMeshes = true;
            public bool RemapAnimationsAndVRCFury = true;
            public bool DeletePlacementLocationBeforeConversion = false;
            public bool DeleteExistingTargetGameObjects = false;
            public bool ClearEditorLogBeforeConversion = false;
        }

        /// <summary>
        /// Automatic texture tuning constants. These replace the old user-facing texture settings:
        /// everything is derived from the target profile's hard caps and then corrected against the
        /// real measured bundle, so the result lands just under the limits rather than guessing.
        /// </summary>
        internal static class TextureAutoTuning
        {
            /// <summary>Stay this fraction under the hard VRAM cap to absorb estimate error (never go over).</summary>
            public const double VramSafetyFraction = 0.03;
            /// <summary>Stay this fraction under the hard bundle cap.</summary>
            public const double BundleSafetyFraction = 0.03;
            /// <summary>
            /// Assumed non-texture (mesh/animation/controller) share of the bundle for the FIRST pass,
            /// before a real build exists to measure it. Replaced by the measured value from then on.
            /// </summary>
            public const double InitialNonTextureShare = 0.45;
            /// <summary>
            /// Preferred resolution floor. Ordering only — every format/crunch option above it is tried
            /// first, but textures can always go below it (down to 32px) rather than miss a budget.
            /// </summary>
            public const int PreferredMinResolution = 512;
            public const int AbsoluteMinResolution = 32;
            /// <summary>
            /// Decimation floor: never reduce an avatar below this fraction of its original triangles,
            /// however far over budget it is. Unlike texture compression, decimation permanently
            /// changes silhouettes and can damage blendshapes.
            /// </summary>
            public const float MinTriangleRetention = 0.25f;
            /// <summary>Downscaling costs more than format detail: keep pixels, grow the block instead.</summary>
            public const float ResolutionPriority = 2.0f;
            /// <summary>Crunch is always offered as a parallel axis; the allocator picks it only when it wins.</summary>
            public const bool AllowCrunch = true;
            public const int CrunchQuality = 50;
        }

        private static Bluscream.TextureCompressor.TextureBudgetRequest BuildTextureRequest(
            ConversionConfig config, PlatformProfile profile, long vramBudget, long diskBudget, GameObject avatarRoot = null)
        {
            // Expression menu icons live on ScriptableObjects, not renderers, so material walking never
            // sees them — yet they are serialized into the bundle and are usually left uncompressed.
            var menuIcons = avatarRoot != null
                ? AvatarSDKEvaluator.CollectMenuIconImportersSafe(avatarRoot)
                : new System.Collections.Generic.List<UnityEditor.TextureImporter>();

            return new Bluscream.TextureCompressor.TextureBudgetRequest
            {
                ExtraTextures = menuIcons,
                VramBudgetBytes = vramBudget,
                DiskBudgetBytes = diskBudget,
                MaxResolution = 0, // start every texture at its native resolution; the allocator decides
                MinResolution = TextureAutoTuning.PreferredMinResolution,
                AbsoluteMinResolution = TextureAutoTuning.AbsoluteMinResolution,
                ResolutionPriority = TextureAutoTuning.ResolutionPriority,
                AllowCrunch = TextureAutoTuning.AllowCrunch,
                CrunchQuality = TextureAutoTuning.CrunchQuality,
                Platform = config.Platform == TargetPlatform.Android ? Bluscream.TextureCompressor.TexturePlatform.Android
                         : config.Platform == TargetPlatform.iOS ? Bluscream.TextureCompressor.TexturePlatform.iOS
                         : Bluscream.TextureCompressor.TexturePlatform.Standalone
            };
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

                // Step 5: Texture budget allocation (VRAM + estimated bundle share).
                // Budgets come straight from the profile's hard caps minus a small safety fraction —
                // the goal is to land just under the limits, so no user headroom guessing is involved.
                Bluscream.TextureCompressor.TextureBudgetResult textureResult = null;
                long bundleCapBytes = profile.MaxAssetBundleSizeBytes == long.MaxValue ? 200L * 1024 * 1024 : profile.MaxAssetBundleSizeBytes;
                long textureVramBudget = Math.Max(1024 * 1024L, (long)(profile.MaxTextureMemoryBytes * (1.0 - TextureAutoTuning.VramSafetyFraction)));
                // First pass has no measurement yet, so assume a typical non-texture share; Step 8.5
                // replaces this with the real measured payload after the first build.
                long textureDiskBudget = Math.Max(512 * 1024L, (long)(bundleCapBytes * (1.0 - TextureAutoTuning.InitialNonTextureShare)));

                if (config.OptimizeTextures)
                {
                    progressCallback?.Invoke("Allocating texture budget...", 0.70f);
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 5] Auto-allocating texture budget — VRAM ≤ {textureVramBudget / (1024.0 * 1024.0):F1} MB (cap {profile.MaxTextureMemoryBytes / (1024.0 * 1024.0):F0} MB), initial texture disk ≤ {textureDiskBudget / (1024.0 * 1024.0):F2} MB (cap {bundleCapBytes / (1024.0 * 1024.0):F2} MB, assuming ~{TextureAutoTuning.InitialNonTextureShare * 100:F0}% non-texture payload until measured).");

                    textureResult = Bluscream.TextureCompressor.TextureBudgetOptimizer.Optimize(
                        targetAvatar,
                        BuildTextureRequest(config, profile, textureVramBudget, textureDiskBudget, targetAvatar),
                        (msg) => progressCallback?.Invoke(msg, 0.70f)
                    );

                    summary.texturesOptimized = textureResult.TexturesProcessed;
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 5] Texture budget allocated: {textureResult.Describe()}");
                    if (textureResult.WentBelowPreferredResolution)
                        summary.AddWarning($"{textureResult.TexturesBelowPreferredResolution} texture(s) had to be downscaled below the preferred {TextureAutoTuning.PreferredMinResolution}px floor to meet the budget.");
                    if (!textureResult.VramBudgetMet)
                        summary.AddWarning($"Texture VRAM ({textureResult.EstimatedVramBytes / (1024.0 * 1024.0):F1} MB) still exceeds the budget after maximum compression — reduce texture count or resolution.");
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
                AvatarSDKEvaluator.AvatarStats currentStats = summary.InitialStats;

                try // ensure temp-removed components are ALWAYS restored, even on exception/cancel
                {
                    // Measurement: one real SDK dry-run build plus an SDK-equivalent VRAM evaluation.
                    // Returns null when the build fails, which stops the convergence loop.
                    Func<Bluscream.Budgeting.BudgetSnapshot> measure = () =>
                    {
                        try
                        {
                            bundleSizeBytes = AvatarSDKEvaluator.BuildAvatarAssetBundle(targetAvatar, out bundlePath, (msg) => progressCallback?.Invoke(msg, 0.98f));
                        }
                        catch (InvalidOperationException ex)
                        {
                            Debug.LogError($"[VRCAvatarOptimizerCore] [Step 8.5] ⚠️ CRITICAL: Failed to obtain compressed AssetBundle size — {ex.Message}");
                            summary.AddError("⚠️ CRITICAL: Could not verify compressed bundle size. SDK dry-run was suppressed or failed. Check console for details.");
                            return null;
                        }

                        currentStats = AvatarSDKEvaluator.EvaluateAvatar(targetAvatar);
                        return new Bluscream.Budgeting.BudgetSnapshot
                        {
                            Items = new List<Bluscream.Budgeting.BudgetItem>
                            {
                                new Bluscream.Budgeting.BudgetItem
                                {
                                    Name = AvatarBudgets.Bundle,
                                    Limit = maxBundleBytes,
                                    Actual = bundleSizeBytes,
                                    SafetyFraction = TextureAutoTuning.BundleSafetyFraction
                                },
                                new Bluscream.Budgeting.BudgetItem
                                {
                                    Name = AvatarBudgets.Vram,
                                    Limit = profile.MaxTextureMemoryBytes,
                                    Actual = currentStats.TotalTextureMemoryBytes,
                                    SafetyFraction = TextureAutoTuning.VramSafetyFraction
                                }
                            }
                        };
                    };

                    // Reducers in priority order: textures first (cheap, reversible-ish), meshes only
                    // once textures are exhausted (destructive to silhouettes and blendshapes).
                    var reducers = new List<Bluscream.Budgeting.IBudgetReducer>();
                    TextureBudgetReducer textureReducer = null;
                    if (config.OptimizeTextures && textureResult != null)
                    {
                        textureReducer = new TextureBudgetReducer(
                            targetAvatar,
                            (vramBudget, diskBudget) => BuildTextureRequest(config, profile, vramBudget, diskBudget, targetAvatar),
                            textureResult,
                            (msg) => progressCallback?.Invoke(msg, 0.98f));
                        reducers.Add(textureReducer);
                    }
                    if (config.DecimateMeshes)
                    {
                        reducers.Add(new MeshDecimationReducer(
                            targetAvatar,
                            TextureAutoTuning.MinTriangleRetention,
                            (msg) => progressCallback?.Invoke(msg, 0.99f)));
                    }

                    var convergence = Bluscream.Budgeting.BudgetConvergence.Run(measure, reducers, new Bluscream.Budgeting.BudgetConvergence.Options
                    {
                        MaxAttempts = config.MaxSizeConvergenceAttempts,
                        Log = (m) => Debug.Log($"[VRCAvatarOptimizerCore] [Step 8.5] {m}"),
                        Warn = (m) => Debug.LogWarning($"[VRCAvatarOptimizerCore] [Step 8.5] {m}"),
                        Progress = (m) => progressCallback?.Invoke(m, 0.98f)
                    });

                    if (textureReducer?.LastResult != null)
                    {
                        textureResult = textureReducer.LastResult;
                        summary.texturesOptimized = textureResult.TexturesProcessed;
                    }

                    foreach (string action in convergence.Actions)
                        summary.AddSuccess(action);

                    if (!convergence.Converged)
                    {
                        switch (convergence.Reason)
                        {
                            case Bluscream.Budgeting.BudgetConvergence.StopReason.ReducersExhausted:
                            case Bluscream.Budgeting.BudgetConvergence.StopReason.NoProgress:
                                summary.AddError($"Could not bring the avatar within budget — {convergence.Message} Remaining size is animator/VRCFury data or mesh content below the decimation floor.");
                                break;
                            case Bluscream.Budgeting.BudgetConvergence.StopReason.AttemptsExhausted:
                                summary.AddError($"Ran out of size convergence attempts ({config.MaxSizeConvergenceAttempts}). Raise 'Max Size Convergence Retries' or reduce avatar content. {convergence.Message}");
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
