using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TextureCompressionEditor = global::Bluscream.TextureCompressor.TextureCompressionEditor;

namespace VRCQuestPatcher
{
    /// <summary>
    /// Core conversion pipeline orchestrating the avatar optimization and platform patching process
    /// </summary>
    public static class VRCAvatarOptimizerCore
    {
        public class ConversionConfig
        {
            public TargetPlatform Platform = TargetPlatform.Android;
            public QuestPerformanceRank TargetRank = QuestPerformanceRank.Medium;
            public AssetPlacementLocation PlacementLocation = AssetPlacementLocation.SeparateFolder;
            public PhysBonePruningStrategy PruningStrategy = PhysBonePruningStrategy.DeepestFirst;
            public bool DuplicateAvatar = true;
            public bool AddPlatformSuffixes = true;
            public string AvatarSuffix = " (Quest)";
            public bool RemoveIncompatibleComponents = true;
            public bool ReplaceShaders = true;
            public bool OptimizeTextures = true;
            public int MaxTextureResolution = 2048; // 4096, 2048, 1024, 512, 256, 128
            public int CrunchCompressionQuality = 75; // 0 = No Crunching (ASTC raw), 100 = Max Crunch (lowest file size)
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
                Debug.LogError("[VRCAvatarOptimizerCore] ConvertAvatar called with null avatarRoot!");
                summary.AddError("Avatar root is null");
                return summary;
            }

            Debug.Log($"[VRCAvatarOptimizerCore] ===== Starting Avatar Conversion for '{avatarRoot.name}' =====");
            Debug.Log($"[VRCAvatarOptimizerCore] Config: Platform={config.Platform}, Rank={config.TargetRank}, Duplicate={config.DuplicateAvatar}, ReplaceShaders={config.ReplaceShaders}, OptimizeTextures={config.OptimizeTextures}, PrunePhysBones={config.PruningStrategy}, DecimateMeshes={config.DecimateMeshes}, RemoveIncompatible={config.RemoveIncompatibleComponents}, Animations={config.RemapAnimationsAndVRCFury}");

            summary.InitialStats = AvatarSDKEvaluator.EvaluateAvatar(avatarRoot);
            Debug.Log($"[VRCAvatarOptimizerCore] Initial stats — Tris: {summary.InitialStats.TriangleCount:N0}, TexMem: {summary.InitialStats.TotalTextureMemoryBytes / (1024.0 * 1024.0):F1} MB, MatSlots: {summary.InitialStats.MaterialSlotCount}, PhysBones: {summary.InitialStats.PhysBoneComponentCount}, Colliders: {summary.InitialStats.PhysBoneColliderCount}, CollisionChecks: {summary.InitialStats.PhysBoneCollisionCheckCount}");

            GameObject targetAvatar = avatarRoot;
            PlatformProfile profile = PlatformProfile.GetProfile(config.Platform, config.TargetRank);

            Debug.Log($"[VRCAvatarOptimizerCore] Profile limits — Platform: {profile.Platform}, Rank: {profile.Rank}, Tris: {(profile.MaxTriangles == int.MaxValue ? "Unlimited" : profile.MaxTriangles.ToString("N0"))}, TexMem: {profile.MaxTextureMemoryBytes / (1024.0 * 1024.0):F0} MB, PhysBones: {profile.MaxPhysBoneComponents}, Colliders: {profile.MaxPhysBoneColliders}, CollisionChecks: {profile.MaxPhysBoneCollisionChecks}");

            try
            {
                // Step 1: Duplicate Avatar GameObject & Manage Platform Suffixes / Active State
                if (config.DuplicateAvatar)
                {
                    progressCallback?.Invoke("Duplicating avatar GameObject...", 0.05f);
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 1] Duplicating avatar '{avatarRoot.name}' for target platform '{config.Platform}'...");

                    string cleanName = avatarRoot.name;
                    if (cleanName.EndsWith(" (PC)")) cleanName = cleanName.Substring(0, cleanName.Length - 5);
                    if (cleanName.EndsWith(" (Quest)")) cleanName = cleanName.Substring(0, cleanName.Length - 8);
                    if (cleanName.EndsWith(" (Android)")) cleanName = cleanName.Substring(0, cleanName.Length - 10);

                    string suffix = config.Platform == TargetPlatform.PC ? " (PC)" : " (Quest)";
                    if (config.AddPlatformSuffixes)
                    {
                        Undo.RecordObject(avatarRoot, "Rename Original Avatar");
                        avatarRoot.name = cleanName + (config.Platform == TargetPlatform.PC ? " (Original)" : " (PC)");
                    }

                    Undo.RecordObject(avatarRoot, "Disable Original Avatar");
                    avatarRoot.SetActive(false);

                    targetAvatar = UnityEngine.Object.Instantiate(avatarRoot, avatarRoot.transform.parent);
                    targetAvatar.name = config.AddPlatformSuffixes ? cleanName + suffix : cleanName + (config.AvatarSuffix ?? suffix);

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
                    DuplicateAndReplaceMaterials(targetAvatar, config, summary, materialMap, (msg, prog) => progressCallback?.Invoke(msg, 0.30f + prog * 0.20f));
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
                        config.PlacementLocation == AssetPlacementLocation.SeparateFolder ? "Assets/_QUESTPATCHER/" + targetAvatar.name : null, 
                        (msg) => progressCallback?.Invoke(msg, 0.55f)
                    );
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 4] Animation rewrite complete.");
                }

                // Step 5: Texture Optimization & Memory Budget
                if (config.OptimizeTextures)
                {
                    progressCallback?.Invoke("Optimizing texture memory budget...", 0.70f);
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 5] Optimizing textures — VRAM budget: {profile.MaxTextureMemoryBytes / (1024.0 * 1024.0):F0} MB");
                    int texCount = TextureCompressionEditor.OptimizeForTextureMemoryBudget(
                        targetAvatar, 
                        profile.MaxTextureMemoryBytes, 
                        (msg) => progressCallback?.Invoke(msg, 0.70f),
                        config.MaxTextureResolution,
                        config.CrunchCompressionQuality
                    );
                    summary.texturesOptimized = texCount;
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 5] Texture optimization complete: {texCount} texture(s) reimported.");
                }

                // Step 6: PhysBone Budget Pruner
                if (config.PruningStrategy != PhysBonePruningStrategy.Disabled)
                {
                    progressCallback?.Invoke("Pruning PhysBones to hit target rank limits...", 0.85f);
                    Debug.Log($"[VRCAvatarOptimizerCore] [Step 6] Pruning PhysBones — strategy={config.PruningStrategy}, target: ≤{profile.MaxPhysBoneComponents} PBs, ≤{profile.MaxPhysBoneColliders} colliders, ≤{profile.MaxPhysBoneCollisionChecks} collision checks.");
                    int pruned = AvatarPhysBonePruner.PrunePhysBones(targetAvatar, profile, (msg) => progressCallback?.Invoke(msg, 0.85f));
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

                // Step 8.5: Dry-Run AssetBundle Build & Verification
                progressCallback?.Invoke("Building dry-run AssetBundle to verify compressed bundle size...", 0.98f);
                Debug.Log($"[VRCAvatarOptimizerCore] [Step 8.5] Running dry-run AssetBundle build verification for '{targetAvatar.name}'...");
                long bundleSizeBytes = AvatarSDKEvaluator.GetBuiltBundleSize(out string bundlePath);
                summary.AssetBundleSizeBytes = bundleSizeBytes;
                if (bundleSizeBytes > 0)
                {
                    double bundleMB = bundleSizeBytes / (1024.0 * 1024.0);
                    if (config.Platform == TargetPlatform.Android && bundleSizeBytes > (long)(10.00 * 1024 * 1024))
                    {
                        Debug.LogWarning($"[VRCAvatarOptimizerCore] [Step 8.5] ⚠️ WARNING: Built AssetBundle size is {bundleMB:F2} MB (exceeds VRChat Quest 10.00 MB limit!).");
                        summary.AddError($"AssetBundle size ({bundleMB:F2} MB) exceeds VRChat Quest 10.00 MB limit!");
                    }
                    else
                    {
                        Debug.Log($"[VRCAvatarOptimizerCore] [Step 8.5] ✓ Verified AssetBundle size: {bundleMB:F2} MB. Bundle file: {bundlePath}");
                        summary.AddSuccess($"Verified AssetBundle size: {bundleMB:F2} MB.");
                    }
                }

                AvatarSDKEvaluator.AvatarStats stats = AvatarSDKEvaluator.EvaluateAvatar(targetAvatar);
                summary.FinalStats = stats;

                Debug.Log($"<color=cyan><b>================================================================================</b></color>");
                Debug.Log($"<color=cyan><b>[VRCAvatarOptimizerCore] BEFORE Conversion Report for '{avatarRoot.name}':</b></color>");
                AvatarSDKEvaluator.PrintSDKAlertsToConsole(avatarRoot, summary.InitialStats);

                Debug.Log($"<color=cyan><b>================================================================================</b></color>");
                Debug.Log($"<color=cyan><b>[VRCAvatarOptimizerCore] AFTER Conversion Report for '{targetAvatar.name}':</b></color>");
                AvatarSDKEvaluator.PrintSDKAlertsToConsole(targetAvatar, stats);

                summary.PrintConsoleSummary(targetAvatar.name, profile);

                Debug.Log($"[VRCAvatarOptimizerCore] ===== Conversion Complete for '{targetAvatar.name}' — {summary.materialsReplaced} mats replaced, {summary.texturesOptimized} textures compressed, {summary.componentsRemoved} components removed =====");
                progressCallback?.Invoke("Conversion completed successfully!", 1.0f);
            }
            catch (Exception e)
            {
                summary.AddError($"Conversion failed: {e.Message}\n{e.StackTrace}");
                Debug.LogError($"[VRCAvatarOptimizerCore] Conversion FAILED for '{avatarRoot.name}': {e.Message}\n{e.StackTrace}");
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

                Material[] mats = entry.renderer.sharedMaterials;
                mats[entry.materialIndex] = questMat;
                Undo.RecordObject(entry.renderer, "Assign Optimized Material");
                entry.renderer.sharedMaterials = mats;
            }
        }

        private static Material DuplicateMaterial(Material srcMat, bool saveInSameFolder, string avatarName)
        {
            if (srcMat == null) return null;

            string srcPath = AssetDatabase.GetAssetPath(srcMat);
            bool isBuiltIn = string.IsNullOrEmpty(srcPath) || srcPath.Contains("unity_builtin_extra") || srcPath.StartsWith("Resources/");

            string filename = !string.IsNullOrEmpty(srcMat.name) ? srcMat.name : "Material";
            if (filename.EndsWith(" (Quest)") || filename.EndsWith(" (Optimized)"))
            {
                Debug.Log($"[VRCAvatarOptimizerCore] Material '{srcMat.name}' already has optimized suffix — skipping duplicate.");
                return srcMat;
            }

            string dir = "Assets/_QUESTPATCHER/" + avatarName;
            if (saveInSameFolder && !isBuiltIn)
            {
                string srcDir = Path.GetDirectoryName(srcPath);
                if (!string.IsNullOrEmpty(srcDir)) dir = srcDir;
            }
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string destPath = Path.Combine(dir, filename + " (Quest).mat").Replace('\\', '/');
            if (File.Exists(destPath))
            {
                Debug.Log($"[VRCAvatarOptimizerCore] Material already exists, reusing: {destPath}");
                return AssetDatabase.LoadAssetAtPath<Material>(destPath);
            }

            if (isBuiltIn)
            {
                Debug.Log($"[VRCAvatarOptimizerCore] Duplicating built-in material '{srcMat.name}' → {destPath}");
                Material newMat = new Material(srcMat);
                AssetDatabase.CreateAsset(newMat, destPath);
                return newMat;
            }

            Debug.Log($"[VRCAvatarOptimizerCore] Copying material '{srcMat.name}' from '{srcPath}' → '{destPath}'");
            AssetDatabase.CopyAsset(srcPath, destPath);
            return AssetDatabase.LoadAssetAtPath<Material>(destPath);
        }

        private static void ReplaceShaderOnMaterial(Material srcMat, Material questMat, ConversionSummary summary)
        {
            if (questMat == null || questMat.shader == null) return;
            string originalShaderName = questMat.shader.name;

            if (originalShaderName.StartsWith("VRChat/Mobile/", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[VRCAvatarOptimizerCore] Material '{questMat.name}' already uses mobile shader '{originalShaderName}' — skipping.");
                summary.materialsSkipped++;
                return;
            }

            var replacement = ShaderMapping.FindReplacementShader(originalShaderName);
            if (replacement.Success && replacement.ReplacementShader != null)
            {
                Debug.Log($"[VRCAvatarOptimizerCore] Shader swap: '{originalShaderName}' → '{replacement.ReplacementShader.name}' on '{questMat.name}'");
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
                Debug.LogWarning($"[VRCAvatarOptimizerCore] No Quest shader mapping for '{originalShaderName}' on material '{questMat.name}'. Add an entry to ShaderMapping to fix this.");
                summary.materialsFailed++;
                summary.AddError($"Could not find Quest replacement for shader: {originalShaderName} on material {questMat.name}");
            }
        }
    }
}
