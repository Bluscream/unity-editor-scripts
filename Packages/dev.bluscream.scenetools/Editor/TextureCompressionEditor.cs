using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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

        // DON'T EDIT THIS
        [MenuItem("Bluscream/Texture Compressor/Texture Compression Editor")]
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
        /// </summary>
        public static int OptimizeForTextureMemoryBudget(
            GameObject avatarRoot, 
            long targetMaxBytes, 
            int defaultMaxSize = 1024, 
            System.Action<string> progressCallback = null)
        {
            if (avatarRoot == null) return 0;

            HashSet<TextureImporter> importers = new HashSet<TextureImporter>();
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

            int optimizedCount = 0;
            int maxSize = (targetMaxBytes <= 40 * 1024 * 1024L) ? Math.Min(defaultMaxSize, 512) : defaultMaxSize;
            int total = importers.Count;
            int index = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (TextureImporter importer in importers)
                {
                    index++;
                    progressCallback?.Invoke($"Compressing texture for Quest ({index}/{total}): {System.IO.Path.GetFileName(importer.assetPath)}");

                    Undo.RecordObject(importer, "Optimize Quest Texture");
                    importer.textureCompression = TextureImporterCompression.Compressed;
                    importer.maxTextureSize = Math.Min(importer.maxTextureSize, maxSize);

                    TextureImporterPlatformSettings androidSettings = importer.GetPlatformTextureSettings("Android");
                    androidSettings.overridden = true;
                    androidSettings.name = "Android";
                    androidSettings.maxTextureSize = Math.Min(androidSettings.maxTextureSize > 0 ? androidSettings.maxTextureSize : importer.maxTextureSize, maxSize);
                    androidSettings.format = TextureImporterFormat.ASTC_6x6;
                    androidSettings.textureCompression = TextureImporterCompression.Compressed;
                    androidSettings.crunchedCompression = true;
                    androidSettings.compressionQuality = 50;

                    importer.SetPlatformTextureSettings(androidSettings);
                    importer.SaveAndReimport();
                    optimizedCount++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[TextureCompressor] Compressed {optimizedCount} textures for Android/Quest platform.");
            return optimizedCount;
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
                Bluscream.BackupConfig config = new Bluscream.BackupConfig
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

                return Bluscream.BackupSystem.CreateBackup(config, Bluscream.BackupScope.AllAssets, null, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to create backup: {e.Message}");
                return null;
            }
        }
    }
}
