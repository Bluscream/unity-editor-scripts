using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Bluscream.BackupSystem;

namespace Bluscream.TextureCompressor
{
    /// <summary>
    /// Editor window for applying texture compression settings
    /// </summary>
    public class TextureCompressionEditor : EditorWindow
    {
        public class CompressorTexture
        {
            public string guid { get; set; }
            public string path { get; set; }
            public TextureImporter importer { get; set; }

            public bool apply(CompressionSettings settings, bool force = false)
            {
                try
                {
                    if (importer == null)
                        return false;

                    if (!force && !settings.validate(importer, path, guid))
                        return false;
                    
                    importer.textureCompression = settings.compression;
                    importer.maxTextureSize = settings.maxTextureSize;
                    
                    if (settings.overrides != null)
                    {
                        foreach (string _override in settings.overrides)
                        {
                            try
                            {
                                // Use new API for Unity 2018.1+
                                #if UNITY_2018_1_OR_NEWER
                                TextureImporterPlatformSettings platformSettings = new TextureImporterPlatformSettings();
                                platformSettings.name = _override;
                                platformSettings.maxTextureSize = settings.maxTextureSize;
                                platformSettings.format = settings.format;
                                platformSettings.compressionQuality = settings.compressorQuality;
                                platformSettings.textureCompression = settings.compression;
                                platformSettings.crunchedCompression = settings.useCrunchCompression;
                                importer.SetPlatformTextureSettings(platformSettings);
                                #else
                                // Fallback to old API for older Unity versions
                                importer.SetPlatformTextureSettings(
                                    _override,
                                    settings.maxTextureSize,
                                    settings.format,
                                    settings.compressorQuality,
                                    settings.useCrunchCompression
                                );
                                #endif
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogWarning($"Error setting platform texture settings for {path} platform {_override}: {e.Message}");
                            }
                        }
                    }
                    
                    importer.SaveAndReimport();
                    return true;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error applying compression to texture {path}: {e.Message}\n{e.StackTrace}");
                    return false;
                }
            }
        }

        public class CompressionSettings
        {
            public string name = "Unknown";
            public int maxTextureSize = 2048;
            public TextureResizeAlgorithm resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            public TextureImporterFormat format = TextureImporterFormat.Automatic;
            public TextureImporterCompression compression = TextureImporterCompression.Compressed;
            public bool useCrunchCompression = false;
            public int compressorQuality = 50;
            public string[] overrides = { };
            public Func<TextureImporter, string, string, bool> validate = (
                TextureImporter _,
                string _a,
                string _b
            ) =>
            {
                return _ != null;
            };

            public List<CompressorTexture> get()
            {
                try
                {
                    List<CompressorTexture> ret = new List<CompressorTexture>();
                    string[] textureGUIDs = AssetDatabase.FindAssets("t:Texture");
                    foreach (string guid in textureGUIDs)
                    {
                        try
                        {
                            string path = AssetDatabase.GUIDToAssetPath(guid);
                            if (string.IsNullOrEmpty(path))
                                continue;

                            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                            if (importer != null && validate(importer, path, guid))
                                ret.Add(
                                    new CompressorTexture()
                                    {
                                        guid = guid,
                                        path = path,
                                        importer = importer,
                                    }
                                );
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogWarning($"Error processing texture with GUID {guid}: {e.Message}");
                        }
                    }
                    return ret;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error getting textures: {e.Message}\n{e.StackTrace}");
                    return new List<CompressorTexture>();
                }
            }

            public bool apply(bool force = false)
            {
                try
                {
                    var success = true;
                    foreach (var texture in get())
                    {
                        try
                        {
                            if (!texture.apply(this, force))
                                success = false;
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogWarning($"Error applying compression to texture {texture.path}: {e.Message}");
                            success = false;
                        }
                    }
                    return success;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error applying compression settings: {e.Message}\n{e.StackTrace}");
                    return false;
                }
            }
        }

        // EDIT THIS - Add your compression profiles here
        internal CompressionSettings[] compressors = new CompressionSettings[]
        {
            new CompressionSettings()
            {
                name = "Normal Maps",
                validate = (TextureImporter importer, string path, string guid) =>
                {
                    return importer.textureType == TextureImporterType.NormalMap;
                },
            },
            new CompressionSettings()
            {
                name = "Remaining Textures",
                useCrunchCompression = true,
                compressorQuality = 75,
                validate = (TextureImporter importer, string path, string guid) =>
                {
                    return importer.textureType != TextureImporterType.NormalMap;
                },
            },
        };

        [MenuItem("Bluscream/VRChat/Texture Compression Editor")]
        [MenuItem("GameObject/VRCAvatarOptimizer/Open Texture Compression Window", false, 43)]
        public static void ShowWindow()
        {
            TextureCompressionEditor window = GetWindow<TextureCompressionEditor>();
            window.titleContent = new GUIContent("Texture Compression Editor");
            window.Show();
        }

        private void CreateCompressionSettingsPanel(CompressionSettings settings, string title = null)
        {
            EditorGUILayout.LabelField(title ?? settings.name, EditorStyles.boldLabel);
            settings.maxTextureSize = EditorGUILayout.IntField(
                "Max Texture Size",
                settings.maxTextureSize
            );
            // settings.resizeAlgorithm = (TextureResizeAlgorithm)EditorGUILayout.EnumPopup("Resize Algorithm", settings.resizeAlgorithm);
            settings.format = (TextureImporterFormat)
                EditorGUILayout.EnumPopup("Format", settings.format);
            settings.compression = (TextureImporterCompression)
                EditorGUILayout.EnumPopup("Compression", settings.compression);
            settings.useCrunchCompression = EditorGUILayout.Toggle(
                "Use Crunch Compression",
                settings.useCrunchCompression
            );
            settings.compressorQuality = EditorGUILayout.IntSlider(
                "Compressor Quality",
                settings.compressorQuality,
                0,
                100
            );
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Compression Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            foreach (var settings in compressors)
            {
                CreateCompressionSettingsPanel(settings, $"{settings.name} Settings");
                EditorGUILayout.Space();
            }

            // Show backup status
            if (Utils.IsBackupSystemAvailable())
            {
                EditorGUILayout.HelpBox("Backup System is available. A backup will be created before applying compression.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("Backup System is not available. Consider installing dev.bluscream.backupsystem for automatic backups.", MessageType.Warning);
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Apply Compression Settings"))
            {
                ApplyCompressionSettings();
            }
        }

        private void ApplyCompressionSettings()
        {
            // Create backup if BackupSystem is available
            string backupPath = null;
            if (Utils.IsBackupSystemAvailable())
            {
                backupPath = BackupSystemHelper.CreateBackupForAllTextures("Texture Compression");
                if (backupPath != null)
                {
                    Debug.Log($"Backup created before compression: {backupPath}");
                }
            }

            var projectTextureCount = AssetDatabase.FindAssets("t:Texture").LongLength;
            foreach (var compressor in compressors)
            {
                long i = 0;
                var textures = compressor.get();
                var compressorTextureCount = textures.Count;
                foreach (var tex in textures)
                {
                    Debug.Log(
                        $"Compressing texture {i}/{compressorTextureCount} ({tex.importer.textureType})"
                    );
                    var success = tex.apply(compressor, true);
                    float progress = (float)i / compressorTextureCount;
                    if (
                        EditorUtility.DisplayCancelableProgressBar(
                            $"Compressing {compressor.name}",
                            $"Compressing texture {i}/{compressorTextureCount}",
                            progress
                        )
                    )
                    {
                        break;
                    }
                    i++;
                }
            }

            EditorUtility.ClearProgressBar();
            Debug.Log($"Compressed {projectTextureCount} Textures" + 
                (backupPath != null ? $". Backup created: {backupPath}" : ""));
        }

        /// <summary>
        /// Public API to optimize all avatar textures to fit within a target texture memory budget (in bytes)
        /// crunchCompressionRatio: 0 = No Crunching (Raw ASTC), 100 = Max Crunching (lowest file size)
        /// </summary>
        public static int OptimizeForTextureMemoryBudget(
            GameObject avatarRoot, 
            long vramBudgetBytes,
            System.Action<string> progressCallback = null,
            int maxResolutionCap = 2048,
            int crunchCompressionRatio = 75)
        {
            if (avatarRoot == null) return 0;

            HashSet<TextureImporter> importers = GetUniqueTextureImporters(avatarRoot);
            if (importers.Count == 0) return 0;

            // VRChat hard limits for Quest/Android
            const long QUEST_VRAM_BUDGET_BYTES  = 40L * 1024 * 1024;  // 40 MB unpacked

            // Convert user Crunch Ratio (0-100%, higher = more crunch) into Unity Crunch Quality (100-0%, lower = more crunch)
            bool isCrunchEnabled = crunchCompressionRatio > 0;
            int unityCrunchQuality = Math.Max(0, Math.Min(100, 100 - crunchCompressionRatio));

            // Use the caller's budget but never exceed the VRChat hard cap
            long effectiveVramBudget = Math.Min(vramBudgetBytes, QUEST_VRAM_BUDGET_BYTES);
            // Leave 1 MB headroom for mesh and animation data
            effectiveVramBudget = Math.Max(1024 * 1024L, effectiveVramBudget - (1024 * 1024L));
            // Target up to 9.0 MB for packed AssetBundle to leave headroom for mesh geometry, materials, and animator clips
            long effectiveBundleBudget = (long)(9.0 * 1024 * 1024);

            Debug.Log($"[TextureCompressor] Budgets — VRAM: {effectiveVramBudget / (1024.0 * 1024.0):F1} MB, Bundle: {effectiveBundleBudget / (1024.0 * 1024.0):F2} MB ({importers.Count} unique textures), MaxResCap: {maxResolutionCap}px, Crunch: {crunchCompressionRatio}% (Unity Quality: {unityCrunchQuality}%)");

            // Define ASTC Compression Profiles: (Format, CrunchQuality, DisplayName, EstimatedCrunchRatio)
            var compressionSteps = new (TextureImporterFormat format, int quality, string name, double crunchRatio)[]
            {
                (TextureImporterFormat.ASTC_4x4,   100, "ASTC 4x4  q=100", 1.00),
                (TextureImporterFormat.ASTC_5x5,    85, "ASTC 5x5  q=85",  0.90),
                (TextureImporterFormat.ASTC_6x6,    75, "ASTC 6x6  q=75",  0.80),
                (TextureImporterFormat.ASTC_8x8,    50, "ASTC 8x8  q=50",  0.70),
                (TextureImporterFormat.ASTC_12x12,  unityCrunchQuality, $"ASTC 12x12 (Crunch {crunchCompressionRatio}%)", isCrunchEnabled ? 0.65 : 1.00),
            };

            int[] allResolutionLimits = new int[] { 4096, 2048, 1024, 512, 256, 128 };
            var resolutionLimits = allResolutionLimits.Where(r => r <= maxResolutionCap).ToArray();
            if (resolutionLimits.Length == 0) resolutionLimits = new int[] { maxResolutionCap };

            int bestResolutionCap = resolutionLimits[resolutionLimits.Length - 1];
            TextureImporterFormat bestFormat = TextureImporterFormat.ASTC_12x12;
            int bestQuality = unityCrunchQuality;

            bool budgetAchieved = false;
            foreach (int maxRes in resolutionLimits)
            {
                foreach (var step in compressionSteps)
                {
                    long vramEstimate   = EstimateTotalTextureMemory(importers, maxRes, step.format);
                    long bundleEstimate = (long)(vramEstimate * step.crunchRatio);

                    bool vramOk   = vramEstimate   <= effectiveVramBudget;
                    bool bundleOk = bundleEstimate <= effectiveBundleBudget;

                    Debug.Log($"[TextureCompressor] {maxRes}px {step.name}: " +
                              $"VRAM ~{vramEstimate / (1024.0 * 1024.0):F2} MB [{(vramOk ? "OK" : "OVER")}], " +
                              $"Bundle ~{bundleEstimate / (1024.0 * 1024.0):F2} MB [{(bundleOk ? "OK" : "OVER")}]");

                    if (vramOk && bundleOk)
                    {
                        bestResolutionCap = maxRes;
                        bestFormat        = step.format;
                        bestQuality       = step.quality;
                        budgetAchieved    = true;
                        Debug.Log($"[TextureCompressor] ✓ Selected: {maxRes}px {step.name} — " +
                                  $"VRAM ~{vramEstimate / (1024.0 * 1024.0):F2} MB, Bundle ~{bundleEstimate / (1024.0 * 1024.0):F2} MB");
                        break;
                    }
                }
                if (budgetAchieved) break;
            }

            if (!budgetAchieved)
            {
                Debug.LogWarning($"[TextureCompressor] Could not meet dual budget within any resolution/format — applying minimum: 128px ASTC_12x12 Crunch 25%. Bundle may still exceed 10 MB.");
            }

            ApplyTextureSettings(importers, bestResolutionCap, bestFormat, bestQuality, progressCallback);
            return importers.Count;
        }

        public static void ApplyTextureSettings(
            HashSet<TextureImporter> importers,
            int maxResolutionCap,
            TextureImporterFormat format,
            int compressionQuality,
            System.Action<string> progressCallback = null)
        {
            int total = importers.Count;
            int index = 0;
            var pathsToReimport = new System.Collections.Generic.List<string>();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (TextureImporter importer in importers)
                {
                    index++;
                    progressCallback?.Invoke($"Optimizing texture ({index}/{total}): {System.IO.Path.GetFileName(importer.assetPath)}");

                    Undo.RecordObject(importer, "Optimize Quest Texture");

                    TextureImporterPlatformSettings androidSettings = importer.GetPlatformTextureSettings("Android");
                    androidSettings.overridden = true;
                    androidSettings.name = "Android";
                    androidSettings.maxTextureSize = maxResolutionCap;
                    androidSettings.format = format;
                    androidSettings.textureCompression = TextureImporterCompression.Compressed;
                    androidSettings.crunchedCompression = compressionQuality > 0 && compressionQuality < 100;
                    androidSettings.compressionQuality = compressionQuality;

                    importer.SetPlatformTextureSettings(androidSettings);
                    importer.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            Debug.Log($"[TextureCompressor] Done: {importers.Count} texture(s) set to {maxResolutionCap}px {format} Crunch {compressionQuality}%.");
        }

        [MenuItem("Bluscream/VRChat/Reset Default PC Texture Settings for Selection")]
        [MenuItem("GameObject/VRCAvatarOptimizer/Reset PC Texture Settings", false, 40)]
        public static void ResetDefaultPCTexturesForSelection()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("[TextureCompressor] Please select an avatar GameObject first.");
                return;
            }

            HashSet<TextureImporter> importers = GetUniqueTextureImporters(selected);
            int count = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (TextureImporter imp in importers)
                {
                    Undo.RecordObject(imp, "Reset PC Max Texture Size");
                    imp.maxTextureSize = 4096;
                    imp.crunchedCompression = false;
                    imp.textureCompression = TextureImporterCompression.Uncompressed;

                    TextureImporterPlatformSettings standaloneSettings = imp.GetPlatformTextureSettings("Standalone");
                    if (standaloneSettings != null && standaloneSettings.overridden)
                    {
                        standaloneSettings.crunchedCompression = false;
                        standaloneSettings.maxTextureSize = 4096;
                        standaloneSettings.textureCompression = TextureImporterCompression.Uncompressed;
                        imp.SetPlatformTextureSettings(standaloneSettings);
                    }

                    imp.SaveAndReimport();
                    count++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TextureCompressor] Reset default PC texture settings (Max Size 4096, Uncompressed, No Crunch) for {count} textures.");
        }

        [MenuItem("Bluscream/VRChat/Clear Android Platform Overrides for Selection")]
        [MenuItem("GameObject/VRCAvatarOptimizer/Clear Android Overrides", false, 41)]
        public static void ClearAndroidOverridesForSelection()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("[TextureCompressor] Please select an avatar GameObject first.");
                return;
            }

            HashSet<TextureImporter> importers = GetUniqueTextureImporters(selected);
            int count = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (TextureImporter imp in importers)
                {
                    Undo.RecordObject(imp, "Clear Android Platform Override");
                    TextureImporterPlatformSettings androidSettings = imp.GetPlatformTextureSettings("Android");
                    if (androidSettings != null && androidSettings.overridden)
                    {
                        androidSettings.overridden = false;
                        imp.SetPlatformTextureSettings(androidSettings);
                        imp.SaveAndReimport();
                        count++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TextureCompressor] Cleared Android platform overrides for {count} textures.");
        }

        [MenuItem("Bluscream/VRChat/Optimize PC Textures (2K Max, 75% Crunch) for Selection")]
        [MenuItem("GameObject/VRCAvatarOptimizer/Optimize PC Textures (2K Max, 75% Crunch)", false, 42)]
        public static void OptimizePCTexturesForSelection()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("[TextureCompressor] Please select an avatar GameObject first.");
                return;
            }

            HashSet<TextureImporter> importers = GetUniqueTextureImporters(selected);
            int count = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (TextureImporter imp in importers)
                {
                    Undo.RecordObject(imp, "Optimize PC Textures");
                    int maxRes = 2048;

                    TextureImporterPlatformSettings standaloneSettings = imp.GetPlatformTextureSettings("Standalone");
                    standaloneSettings.overridden = true;
                    standaloneSettings.name = "Standalone";
                    standaloneSettings.maxTextureSize = Math.Min(standaloneSettings.maxTextureSize > 0 ? standaloneSettings.maxTextureSize : maxRes, maxRes);
                    standaloneSettings.textureCompression = TextureImporterCompression.Compressed;
                    standaloneSettings.crunchedCompression = true;
                    standaloneSettings.compressionQuality = 75;
                    imp.SetPlatformTextureSettings(standaloneSettings);

                    imp.SaveAndReimport();
                    count++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TextureCompressor] Optimized {count} PC textures (2048px max cap, DXT Crunch 75%).");
        }

        public static HashSet<TextureImporter> GetUniqueTextureImporters(GameObject avatarRoot)
        {
            HashSet<TextureImporter> importers = new HashSet<TextureImporter>();
            if (avatarRoot == null) return importers;

            Renderer[] renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    Shader s = m.shader;
                    int count = ShaderUtil.GetPropertyCount(s);
                    for (int i = 0; i < count; i++)
                    {
                        if (ShaderUtil.GetPropertyType(s, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                        {
                            Texture tex = m.GetTexture(ShaderUtil.GetPropertyName(s, i));
                            if (tex != null)
                            {
                                string path = AssetDatabase.GetAssetPath(tex);
                                if (!string.IsNullOrEmpty(path))
                                {
                                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                                    if (importer != null) importers.Add(importer);
                                }
                            }
                        }
                    }
                }
            }
            return importers;
        }

        private static long EstimateTotalTextureMemory(HashSet<TextureImporter> importers, int maxResCap, TextureImporterFormat format)
        {
            // Exact BPP values from Thry VRAM Calculator (TextureVRAM.cs)
            double bytesPerPixel = 1.0;
            switch (format)
            {
                case TextureImporterFormat.ASTC_4x4: bytesPerPixel = 8.0 / 8.0; break;     // 1.000 BPP
                case TextureImporterFormat.ASTC_5x5: bytesPerPixel = 5.12 / 8.0; break;    // 0.640 BPP
                case TextureImporterFormat.ASTC_6x6: bytesPerPixel = 3.55 / 8.0; break;    // 0.44375 BPP
                case TextureImporterFormat.ASTC_8x8: bytesPerPixel = 2.00 / 8.0; break;    // 0.250 BPP
                case TextureImporterFormat.ASTC_10x10: bytesPerPixel = 1.28 / 8.0; break;  // 0.160 BPP
                case TextureImporterFormat.ASTC_12x12: bytesPerPixel = 1.00 / 8.0; break;  // 0.125 BPP
            }

            long total = 0;
            foreach (var imp in importers)
            {
                int srcWidth = maxResCap;
                int srcHeight = maxResCap;
                try
                {
                    MethodInfo getSourceSizeMethod = typeof(TextureImporter).GetMethod("GetSourceTextureWidthAndHeight", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (getSourceSizeMethod != null)
                    {
                        object[] args = new object[] { 0, 0 };
                        getSourceSizeMethod.Invoke(imp, args);
                        int w = (int)args[0];
                        int h = (int)args[1];
                        if (w > 0 && h > 0)
                        {
                            srcWidth = w;
                            srcHeight = h;
                        }
                    }
                }
                catch { }

                // Scale dimensions while maintaining aspect ratio, capped at maxResCap
                double scale = Math.Min(1.0, (double)maxResCap / Math.Max(srcWidth, srcHeight));
                int targetWidth = Math.Max(1, (int)(srcWidth * scale));
                int targetHeight = Math.Max(1, (int)(srcHeight * scale));

                double mipMapMultiplier = imp.mipmapEnabled ? 1.33333 : 1.0;
                total += (long)(targetWidth * targetHeight * bytesPerPixel * mipMapMultiplier);
            }
            return total;
        }
    }

    /// <summary>
    /// Helper class to use BackupSystem for creating backups before compressing textures
    /// </summary>
    internal static class BackupSystemHelper
    {
        public static string CreateBackupForAllTextures(string backupName)
        {
            try
            {
                BackupConfig config = new BackupConfig
                {
                    backupMaterials = false,
                    backupComponents = false,
                    backupTextures = true,
                    backupGameObjectHierarchy = false,
                    includeMaterialProperties = false,
                    includeComponentData = false,
                    backupLocation = "Assets/TextureCompressorBackups",
                    backupName = backupName
                };

                return global::Bluscream.BackupSystem.BackupSystem.CreateBackup(config, BackupScope.AllAssets, null, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to create backup: {e.Message}");
                return null;
            }
        }
    }
}
