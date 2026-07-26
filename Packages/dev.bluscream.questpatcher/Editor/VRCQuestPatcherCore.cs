using System;
using System.Collections.Generic;
using System.IO;
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
            public bool DecimateMeshes = true;
            public bool PrunePhysBones = true;
            public bool RemapAnimationsAndVRCFury = true;
            public int MaxTextureSize = 1024;
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
                summary.AddError("Avatar root is null");
                return summary;
            }

            summary.InitialStats = QuestSDKEvaluator.EvaluateAvatar(avatarRoot);
            GameObject targetAvatar = avatarRoot;
            QuestPerformanceProfile profile = QuestPerformanceProfile.GetProfile(config.TargetRank);
            profile.Placement = config.PlacementLocation;
            profile.PruningStrategy = config.PruningStrategy;

            try
            {
                // Step 1: Duplicate Avatar GameObject & Manage Platform Suffixes / Active State
                if (config.DuplicateAvatar)
                {
                    progressCallback?.Invoke("Duplicating avatar GameObject for Quest...", 0.05f);

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
                    summary.AddSuccess($"Created Quest Avatar clone: {targetAvatar.name}", targetAvatar);
                }

                // Step 2: Remove Quest-Incompatible Components
                if (config.RemoveIncompatibleComponents)
                {
                    progressCallback?.Invoke("Removing incompatible components...", 0.15f);
                    var removedComps = QuestComponentRemover.RemoveIncompatibleComponents(
                        targetAvatar, 
                        (msg) => progressCallback?.Invoke(msg, 0.15f)
                    );
                    summary.componentsRemoved = removedComps.Count;
                }

                // Step 3: Duplicate Materials & Remap Shaders
                Dictionary<Material, Material> materialMap = new Dictionary<Material, Material>();
                if (config.ReplaceShaders)
                {
                    progressCallback?.Invoke("Duplicating materials and replacing shaders...", 0.30f);
                    DuplicateAndReplaceMaterials(targetAvatar, config, summary, materialMap, (msg, prog) => progressCallback?.Invoke(msg, 0.30f + prog * 0.20f));
                }

                // Step 4: Remap AnimatorControllers, AnimationClips, and VRCFury Components
                if (config.RemapAnimationsAndVRCFury && materialMap.Count > 0)
                {
                    progressCallback?.Invoke("Rewriting Animator, Clips, Material Swaps, and VRCFury...", 0.55f);
                    QuestAnimationRewriter.ProcessAvatarAnimationsAndVRCFury(
                        targetAvatar, 
                        materialMap, 
                        config.PlacementLocation == AssetPlacementLocation.SeparateFolder ? "Assets/QuestPatched/" + targetAvatar.name : null, 
                        (msg) => progressCallback?.Invoke(msg, 0.55f)
                    );
                }

                // Step 5: Texture Optimization & Memory Budget
                if (config.OptimizeTextures)
                {
                    progressCallback?.Invoke("Optimizing texture memory budget for Quest...", 0.70f);
                    int texCount = TextureCompressionEditor.OptimizeForTextureMemoryBudget(
                        targetAvatar, 
                        profile.MaxTextureMemoryBytes, 
                        config.MaxTextureSize, 
                        (msg) => progressCallback?.Invoke(msg, 0.70f)
                    );
                    summary.texturesOptimized = texCount;
                }

                // Step 6: PhysBone Budget Pruner
                if (config.PruningStrategy != PhysBonePruningStrategy.Disabled)
                {
                    progressCallback?.Invoke("Pruning PhysBones to hit target rank limits...", 0.85f);
                    int pruned = QuestPhysBonePruner.PrunePhysBones(targetAvatar, profile, (msg) => progressCallback?.Invoke(msg, 0.85f));
                    summary.AddSuccess($"Pruned {pruned} PhysBone components/colliders to comply with rank '{profile.Rank}'.");
                }

                // Step 7: Mesh Decimation to hit Target Poly Count Limit
                if (config.DecimateMeshes)
                {
                    progressCallback?.Invoke("Decimating avatar meshes to target triangle budget...", 0.92f);
                    int finalTris = Bluscream.MobileDecimater.Editor.MobileDecimationProcessor.DecimateAvatarMeshesToTargetTris(
                        targetAvatar, 
                        profile.MaxTriangles, 
                        (msg) => progressCallback?.Invoke(msg, 0.92f)
                    );
                    summary.AddSuccess($"Mesh decimation complete. Final triangle count: {finalTris} (Target: {profile.MaxTriangles}).");
                }

                // Step 8: Save Assets & Final Rating Check
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Selection.activeGameObject = targetAvatar;
                EditorGUIUtility.PingObject(targetAvatar);

                QuestSDKEvaluator.AvatarStats stats = QuestSDKEvaluator.EvaluateAvatar(targetAvatar);
                summary.FinalStats = stats;

                Debug.Log($"<color=cyan><b>================================================================================</b></color>");
                Debug.Log($"<color=cyan><b>[VRC-QuestPatcher] BEFORE Conversion Report for '{avatarRoot.name}':</b></color>");
                QuestSDKEvaluator.PrintSDKAlertsToConsole(avatarRoot, summary.InitialStats);

                Debug.Log($"<color=cyan><b>================================================================================</b></color>");
                Debug.Log($"<color=cyan><b>[VRC-QuestPatcher] AFTER Conversion Report for '{targetAvatar.name}':</b></color>");
                QuestSDKEvaluator.PrintSDKAlertsToConsole(targetAvatar, stats);

                summary.PrintConsoleSummary(targetAvatar.name, profile);

                progressCallback?.Invoke("Conversion completed successfully!", 1.0f);
            }
            catch (Exception e)
            {
                summary.AddError($"Conversion failed: {e.Message}\n{e.StackTrace}");
                Debug.LogError($"[VRCQuestPatcherCore] Conversion error: {e}");
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
                return srcMat;

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
                return AssetDatabase.LoadAssetAtPath<Material>(destPath);
            }

            if (isBuiltIn)
            {
                Material newMat = new Material(srcMat);
                AssetDatabase.CreateAsset(newMat, destPath);
                return newMat;
            }

            AssetDatabase.CopyAsset(srcPath, destPath);
            return AssetDatabase.LoadAssetAtPath<Material>(destPath);
        }

        private static void ReplaceShaderOnMaterial(Material srcMat, Material questMat, ConversionSummary summary)
        {
            if (questMat == null || questMat.shader == null) return;
            string originalShaderName = questMat.shader.name;

            if (originalShaderName.StartsWith("VRChat/Mobile/", StringComparison.OrdinalIgnoreCase))
            {
                summary.materialsSkipped++;
                return;
            }

            var replacement = ShaderMapping.FindReplacementShader(originalShaderName);
            if (replacement.Success && replacement.ReplacementShader != null)
            {
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
                summary.materialsFailed++;
                summary.AddError($"Could not find Quest replacement for shader: {originalShaderName} on material {questMat.name}");
            }
        }
    }
}
