using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using BluscreamComponentRemover = global::Bluscream.ComponentRemover.ComponentRemover;
using TextureCompressionEditor = global::Bluscream.TextureCompressor.TextureCompressionEditor;

namespace VRCQuestPatcher
{
    /// <summary>
    /// Core conversion pipeline orchestrating the PC-to-Quest avatar patching process
    /// </summary>
    public static class VRCQuestPatcherCore
    {
        public class ConversionConfig
        {
            public QuestPerformanceRank TargetRank = QuestPerformanceRank.Medium;
            public AssetPlacementLocation PlacementLocation = AssetPlacementLocation.SeparateFolder;
            public PhysBonePruningStrategy PruningStrategy = PhysBonePruningStrategy.DeepestFirst;
            public bool DuplicateAvatar = true;
            public bool AddPlatformSuffixes = true;
            public string AvatarSuffix = " (Quest)";
            public bool RemoveIncompatibleComponents = true;
            public bool ReplaceShaders = true;
            public bool OptimizeTextures = true;
            public int MaxTextureResolution = 2048; // 2048, 1024, 512, 256, 128
            public bool EnableCrunchCompression = true;
            public int CrunchCompressionQuality = 25; // 0 (Max Crunch) to 100 (No Crunch / High Quality)
            public bool DecimateMeshes = true;
            public bool PrunePhysBones = true;
            public bool RemapAnimationsAndVRCFury = true;
            public string BackupLocation = "Assets/VRCQuestPatcherBackups";
        }

        public static ConversionSummary ConvertAvatar(
            GameObject avatarRoot, 
            ConversionConfig config, 
            Action<string, float> progressCallback = null)
        {
            ConversionSummary summary = new ConversionSummary();

            if (avatarRoot == null)
            {
                Debug.LogError("[VRCQuestPatcherCore] ConvertAvatar called with null avatarRoot!");
                summary.AddError("Avatar root is null");
                return summary;
            }

            Debug.Log($"[VRCQuestPatcherCore] ===== Starting Quest Conversion for '{avatarRoot.name}' =====");
            Debug.Log($"[VRCQuestPatcherCore] Config: Rank={config.TargetRank}, Duplicate={config.DuplicateAvatar}, ReplaceShaders={config.ReplaceShaders}, OptimizeTextures={config.OptimizeTextures}, PrunePhysBones={config.PruningStrategy}, DecimateMeshes={config.DecimateMeshes}, RemoveIncompatible={config.RemoveIncompatibleComponents}, Animations={config.RemapAnimationsAndVRCFury}");

            summary.InitialStats = QuestSDKEvaluator.EvaluateAvatar(avatarRoot);
            Debug.Log($"[VRCQuestPatcherCore] Initial stats — Tris: {summary.InitialStats.TriangleCount:N0}, TexMem: {summary.InitialStats.TotalTextureMemoryBytes / (1024.0 * 1024.0):F1} MB, MatSlots: {summary.InitialStats.MaterialSlotCount}, PhysBones: {summary.InitialStats.PhysBoneComponentCount}, Colliders: {summary.InitialStats.PhysBoneColliderCount}, CollisionChecks: {summary.InitialStats.PhysBoneCollisionCheckCount}");

            GameObject targetAvatar = avatarRoot;
            QuestPerformanceProfile profile = QuestPerformanceProfile.GetProfile(config.TargetRank);
            profile.Placement = config.PlacementLocation;
            profile.PruningStrategy = config.PruningStrategy;
            Debug.Log($"[VRCQuestPatcherCore] Profile limits — Tris: {(profile.MaxTriangles == int.MaxValue ? "Unlimited" : profile.MaxTriangles.ToString("N0"))}, TexMem: {profile.MaxTextureMemoryBytes / (1024.0 * 1024.0):F0} MB, PhysBones: {profile.MaxPhysBoneComponents}, Colliders: {profile.MaxPhysBoneColliders}, CollisionChecks: {profile.MaxPhysBoneCollisionChecks}");

            try
            {
                // Step 1: Duplicate Avatar GameObject & Manage Platform Suffixes / Active State
                if (config.DuplicateAvatar)
                {
                    progressCallback?.Invoke("Duplicating avatar GameObject for Quest...", 0.05f);
                    Debug.Log($"[VRCQuestPatcherCore] [Step 1] Duplicating avatar '{avatarRoot.name}' for Quest...");

                    string cleanName = avatarRoot.name;
                    if (cleanName.EndsWith(" (PC)")) cleanName = cleanName.Substring(0, cleanName.Length - 5);
                    if (cleanName.EndsWith(" (Quest)")) cleanName = cleanName.Substring(0, cleanName.Length - 8);

                    if (config.AddPlatformSuffixes)
                    {
                        Undo.RecordObject(avatarRoot, "Rename PC Avatar");
                        avatarRoot.name = cleanName + " (PC)";
                    }

                    // Disable PC avatar before conversion
                    Undo.RecordObject(avatarRoot, "Disable PC Avatar");
                    avatarRoot.SetActive(false);

                    targetAvatar = UnityEngine.Object.Instantiate(avatarRoot, avatarRoot.transform.parent);
                    targetAvatar.name = config.AddPlatformSuffixes ? cleanName + " (Quest)" : cleanName + (config.AvatarSuffix ?? " (Quest)");

                    // Enable Quest avatar after duplication
                    targetAvatar.SetActive(true);

                    Undo.RegisterCreatedObjectUndo(targetAvatar, "Create Quest Avatar Clone");
                    Debug.Log($"[VRCQuestPatcherCore] [Step 1] Created Quest clone: '{targetAvatar.name}'");
                    summary.AddSuccess($"Created Quest Avatar clone: {targetAvatar.name}", targetAvatar);
                }
                else
                {
                    Debug.Log($"[VRCQuestPatcherCore] [Step 1] Skipped duplication — editing '{targetAvatar.name}' in-place.");
                }

                // Step 2: Remove Quest-Incompatible Components
                if (config.RemoveIncompatibleComponents)
                {
                    progressCallback?.Invoke("Removing incompatible components...", 0.15f);
                    Debug.Log($"[VRCQuestPatcherCore] [Step 2] Removing Quest-incompatible components from '{targetAvatar.name}'...");
                    var removedComps = QuestComponentRemover.RemoveIncompatibleComponents(
                        targetAvatar, 
                        (msg) => progressCallback?.Invoke(msg, 0.15f)
                    );
                    summary.componentsRemoved = removedComps.Count;
                    Debug.Log($"[VRCQuestPatcherCore] [Step 2] Removed {removedComps.Count} incompatible components.");
                    if (removedComps.Count > 0)
                    {
                        var grouped = new Dictionary<string, int>();
                        foreach (var rc in removedComps) {
                            string t = rc.componentType?.Split('.')?.LastOrDefault() ?? rc.componentType;
                            grouped[t] = grouped.TryGetValue(t, out int v) ? v + 1 : 1;
                        }
                        foreach (var kv in grouped)
                            Debug.Log($"[VRCQuestPatcherCore] [Step 2]   {kv.Value}x {kv.Key}");
                    }
                }
                else
                {
                    Debug.Log($"[VRCQuestPatcherCore] [Step 2] Skipped incompatible component removal (disabled in config).");
                }

                // Step 3: Duplicate Materials & Remap Shaders
                Dictionary<Material, Material> materialMap = new Dictionary<Material, Material>();
                if (config.ReplaceShaders)
                {
                    progressCallback?.Invoke("Duplicating materials and replacing shaders...", 0.30f);
                    Debug.Log($"[VRCQuestPatcherCore] [Step 3] Duplicating materials and remapping shaders on '{targetAvatar.name}'...");
                    DuplicateAndReplaceMaterials(targetAvatar, config, summary, materialMap, (msg, prog) => progressCallback?.Invoke(msg, 0.30f + prog * 0.20f));
                    Debug.Log($"[VRCQuestPatcherCore] [Step 3] Materials processed: {materialMap.Count} unique. Replaced: {summary.materialsReplaced}, Skipped (already mobile): {summary.materialsSkipped}, Failed (no mapping): {summary.materialsFailed}.");
                    if (summary.materialsFailed > 0)
                        Debug.LogWarning($"[VRCQuestPatcherCore] [Step 3] {summary.materialsFailed} material(s) had no Quest shader mapping — they will use original shaders. Check ShaderMapping config.");
                }
                else
                {
                    Debug.Log($"[VRCQuestPatcherCore] [Step 3] Skipped material/shader replacement (disabled in config).");
                }

                // Step 4: Remap AnimatorControllers, AnimationClips, and VRCFury Components
                if (config.RemapAnimationsAndVRCFury && materialMap.Count > 0)
                {
                    progressCallback?.Invoke("Rewriting Animator, Clips, Material Swaps, and VRCFury...", 0.55f);
                    Debug.Log($"[VRCQuestPatcherCore] [Step 4] Rewriting animations/VRCFury for '{targetAvatar.name}' with {materialMap.Count} material remaps...");
                    QuestAnimationRewriter.ProcessAvatarAnimationsAndVRCFury(
                        targetAvatar, 
                        materialMap, 
                        config.PlacementLocation == AssetPlacementLocation.SeparateFolder ? "Assets/QuestPatched/" + targetAvatar.name : null, 
                        (msg) => progressCallback?.Invoke(msg, 0.55f)
                    );
                    Debug.Log($"[VRCQuestPatcherCore] [Step 4] Animation rewrite complete.");
                }
                else if (config.RemapAnimationsAndVRCFury && materialMap.Count == 0)
                {
                    Debug.LogWarning($"[VRCQuestPatcherCore] [Step 4] Skipped animation remap — no materials were duplicated (materialMap is empty). Was ReplaceShaders disabled or did all materials fail?");
                }
                else
                {
                    Debug.Log($"[VRCQuestPatcherCore] [Step 4] Skipped animation/VRCFury remap (disabled in config).");
                }

                // Step 5: Texture Optimization & Memory Budget
                if (config.OptimizeTextures)
                {
                    progressCallback?.Invoke("Optimizing texture memory budget for Quest...", 0.70f);
                    Debug.Log($"[VRCQuestPatcherCore] [Step 5] Optimizing textures — VRAM budget: {profile.MaxTextureMemoryBytes / (1024.0 * 1024.0):F0} MB, Bundle budget: 10 MB (dynamic resolution selection)");
                    int texCount = TextureCompressionEditor.OptimizeForTextureMemoryBudget(
                        targetAvatar, 
                        profile.MaxTextureMemoryBytes, 
                        (msg) => progressCallback?.Invoke(msg, 0.70f),
                        config.MaxTextureResolution,
                        config.EnableCrunchCompression,
                        config.CrunchCompressionQuality
                    );
                    summary.texturesOptimized = texCount;
                    Debug.Log($"[VRCQuestPatcherCore] [Step 5] Texture optimization complete: {texCount} texture(s) reimported.");
                }
                else
                {
                    Debug.Log($"[VRCQuestPatcherCore] [Step 5] Skipped texture optimization (disabled in config).");
                }

                // Step 6: PhysBone Budget Pruner
                if (config.PruningStrategy != PhysBonePruningStrategy.Disabled)
                {
                    progressCallback?.Invoke("Pruning PhysBones to hit target rank limits...", 0.85f);
                    Debug.Log($"[VRCQuestPatcherCore] [Step 6] Pruning PhysBones — strategy={config.PruningStrategy}, target: ≤{profile.MaxPhysBoneComponents} PBs, ≤{profile.MaxPhysBoneColliders} colliders, ≤{profile.MaxPhysBoneCollisionChecks} collision checks.");
                    int pruned = QuestPhysBonePruner.PrunePhysBones(targetAvatar, profile, (msg) => progressCallback?.Invoke(msg, 0.85f));
                    Debug.Log($"[VRCQuestPatcherCore] [Step 6] PhysBone pruning complete: {pruned} component(s)/collider(s) removed.");
                    summary.AddSuccess($"Pruned {pruned} PhysBone components/colliders to comply with rank '{profile.Rank}'.");
                }
                else
                {
                    Debug.Log($"[VRCQuestPatcherCore] [Step 6] Skipped PhysBone pruning (strategy=Disabled).");
                }

                // Step 7: Mesh Decimation to hit Target Poly Count Limit
                if (config.DecimateMeshes)
                {
                    progressCallback?.Invoke("Decimating avatar meshes to target triangle budget...", 0.92f);
                    string triLimitStr = profile.MaxTriangles == int.MaxValue ? "Unlimited" : profile.MaxTriangles.ToString("N0");
                    Debug.Log($"[VRCQuestPatcherCore] [Step 7] Decimating meshes — target triangle limit: {triLimitStr} (current: {summary.InitialStats.TriangleCount:N0}).");
                    int finalTris = Bluscream.MobileDecimater.Editor.MobileDecimationProcessor.DecimateAvatarMeshesToTargetTris(
                        targetAvatar, 
                        profile.MaxTriangles, 
                        (msg) => progressCallback?.Invoke(msg, 0.92f)
                    );
                    Debug.Log($"[VRCQuestPatcherCore] [Step 7] Decimation complete. Final triangle count: {finalTris:N0} (target was {triLimitStr}).");
                    summary.AddSuccess($"Mesh decimation complete. Final triangle count: {finalTris:N0} (Target: {triLimitStr}).");
                }
                else
                {
                    Debug.Log($"[VRCQuestPatcherCore] [Step 7] Skipped mesh decimation (disabled in config).");
                }

                // Step 8.5: Dry-Run AssetBundle Build & Verification
                progressCallback?.Invoke("Building dry-run AssetBundle to verify compressed bundle size...", 0.98f);
                Debug.Log($"[VRCQuestPatcherCore] [Step 8.5] Running dry-run AssetBundle build verification for '{targetAvatar.name}'...");
                long bundleSizeBytes = QuestSDKEvaluator.BuildAvatarBundleDryRun(targetAvatar, out string bundlePath);
                summary.AssetBundleSizeBytes = bundleSizeBytes;
                if (bundleSizeBytes > 0)
                {
                    double bundleMB = bundleSizeBytes / (1024.0 * 1024.0);
                    if (bundleSizeBytes > (long)(10.00 * 1024 * 1024))
                    {
                        Debug.LogWarning($"[VRCQuestPatcherCore] [Step 8.5] ⚠️ WARNING: Built AssetBundle size is {bundleMB:F2} MB (exceeds VRChat Quest 10.00 MB limit!).");
                        summary.AddError($"AssetBundle size ({bundleMB:F2} MB) exceeds VRChat Quest 10.00 MB limit!");
                    }
                    else
                    {
                        Debug.Log($"[VRCQuestPatcherCore] [Step 8.5] ✓ Verified AssetBundle size: {bundleMB:F2} MB (Target budget: ≤ 9.99 MB / Limit: 10.00 MB). Bundle file: {bundlePath}");
                        summary.AddSuccess($"Verified Quest AssetBundle size: {bundleMB:F2} MB (Limit: 10.00 MB).");
                    }
                }

                QuestSDKEvaluator.AvatarStats stats = QuestSDKEvaluator.EvaluateAvatar(targetAvatar);
                summary.FinalStats = stats;

                Debug.Log($"<color=cyan><b>================================================================================</b></color>");
                Debug.Log($"<color=cyan><b>[VRC-QuestPatcher] BEFORE Conversion Report for '{avatarRoot.name}':</b></color>");
                QuestSDKEvaluator.PrintSDKAlertsToConsole(avatarRoot, summary.InitialStats);

                Debug.Log($"<color=cyan><b>================================================================================</b></color>");
                Debug.Log($"<color=cyan><b>[VRC-QuestPatcher] AFTER Conversion Report for '{targetAvatar.name}':</b></color>");
                QuestSDKEvaluator.PrintSDKAlertsToConsole(targetAvatar, stats);

                summary.PrintConsoleSummary(targetAvatar.name, profile);

                Debug.Log($"[VRCQuestPatcherCore] ===== Conversion Complete for '{targetAvatar.name}' — {summary.materialsReplaced} mats replaced, {summary.texturesOptimized} textures compressed, {summary.componentsRemoved} components removed =====");
                progressCallback?.Invoke("Conversion completed successfully!", 1.0f);
            }
            catch (Exception e)
            {
                summary.AddError($"Conversion failed: {e.Message}\n{e.StackTrace}");
                Debug.LogError($"[VRCQuestPatcherCore] Conversion FAILED for '{avatarRoot.name}': {e.Message}\n{e.StackTrace}");
            }

            return summary;
        }

        private static void DuplicateAndReplaceMaterials(
            GameObject avatarRoot, 
            ConversionConfig config, 
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
                    questMat = DuplicateMaterial(srcMat, config.PlacementLocation == AssetPlacementLocation.SameFolderAsOriginal, avatarRoot.name);
                    if (questMat != null)
                    {
                        materialMap[srcMat] = questMat;
                        ReplaceShaderOnMaterial(srcMat, questMat, summary);
                    }
                    else
                    {
                        questMat = srcMat;
                    }
                }

                // Assign duplicated quest material to cloned renderer
                Material[] mats = entry.renderer.sharedMaterials;
                mats[entry.materialIndex] = questMat;
                Undo.RecordObject(entry.renderer, "Assign Quest Material");
                entry.renderer.sharedMaterials = mats;
            }
        }

        private static Material DuplicateMaterial(Material srcMat, bool saveInSameFolder, string avatarName)
        {
            if (srcMat == null) return null;

            string srcPath = AssetDatabase.GetAssetPath(srcMat);
            bool isBuiltIn = string.IsNullOrEmpty(srcPath) || srcPath.Contains("unity_builtin_extra") || srcPath.StartsWith("Resources/");

            string filename = !string.IsNullOrEmpty(srcMat.name) ? srcMat.name : "Material";
            if (filename.EndsWith(" (Quest)"))
            {
                Debug.Log($"[VRCQuestPatcherCore] Material '{srcMat.name}' already has '(Quest)' suffix — skipping duplicate.");
                return srcMat;
            }

            string dir = "Assets/QuestPatched/" + avatarName;
            if (saveInSameFolder && !isBuiltIn)
            {
                string srcDir = Path.GetDirectoryName(srcPath);
                if (!string.IsNullOrEmpty(srcDir)) dir = srcDir;
            }
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string destPath = Path.Combine(dir, filename + " (Quest).mat").Replace('\\', '/');
            if (File.Exists(destPath))
            {
                Debug.Log($"[VRCQuestPatcherCore] Quest material already exists, reusing: {destPath}");
                return AssetDatabase.LoadAssetAtPath<Material>(destPath);
            }

            if (isBuiltIn)
            {
                Debug.Log($"[VRCQuestPatcherCore] Duplicating built-in material '{srcMat.name}' → {destPath}");
                Material newMat = new Material(srcMat);
                AssetDatabase.CreateAsset(newMat, destPath);
                return newMat;
            }

            Debug.Log($"[VRCQuestPatcherCore] Copying material '{srcMat.name}' from '{srcPath}' → '{destPath}'");
            AssetDatabase.CopyAsset(srcPath, destPath);
            return AssetDatabase.LoadAssetAtPath<Material>(destPath);
        }

        private static void ReplaceShaderOnMaterial(Material srcMat, Material questMat, ConversionSummary summary)
        {
            if (questMat == null || questMat.shader == null) return;
            string originalShaderName = questMat.shader.name;

            if (originalShaderName.StartsWith("VRChat/Mobile/", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[VRCQuestPatcherCore] Material '{questMat.name}' already uses mobile shader '{originalShaderName}' — skipping.");
                summary.materialsSkipped++;
                return;
            }

            var replacement = ShaderMapping.FindReplacementShader(originalShaderName);
            if (replacement.Success && replacement.ReplacementShader != null)
            {
                Debug.Log($"[VRCQuestPatcherCore] Shader swap: '{originalShaderName}' → '{replacement.ReplacementShader.name}' on '{questMat.name}'");
                Undo.RegisterCompleteObjectUndo(questMat, "Replace Shader for Quest");
                Material tempMat = new Material(questMat);

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
                Debug.LogWarning($"[VRCQuestPatcherCore] No Quest shader mapping for '{originalShaderName}' on material '{questMat.name}'. Add an entry to ShaderMapping to fix this.");
                summary.materialsFailed++;
                summary.AddError($"Could not find Quest replacement for shader: {originalShaderName} on material {questMat.name}");
            }
        }
    }
}
