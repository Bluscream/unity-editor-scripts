using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using static Bluscream.Utils;

namespace VRCQuestPatcher
{
    /// <summary>
    /// Manages backup creation and restoration for avatar conversion
    /// </summary>
    public static class BackupManager
    {
        [System.Serializable]
        public class MaterialPropertyEntry
        {
            public string propertyName;
            public string propertyType; // "float", "color", "vector", "texture"
            public string propertyValue; // Serialized as string, will be parsed on restore
        }

        [System.Serializable]
        public class MaterialBackup
        {
            public string materialPath;
            public string shaderName;
            public List<MaterialPropertyEntry> materialProperties = new List<MaterialPropertyEntry>();
        }

        [System.Serializable]
        public class ComponentBackup
        {
            public string gameObjectPath;
            public string componentType;
            public string componentData; // JSON serialized component data
        }

        [System.Serializable]
        public class TextureBackup
        {
            public string texturePath;
            public int maxTextureSize;
            public TextureImporterFormat format;
            public TextureImporterCompression compression;
            public bool useCrunchCompression;
            public int compressorQuality;
        }

        [System.Serializable]
        public class BackupData
        {
            public string timestamp;
            public string avatarRootPath;
            public List<MaterialBackup> materials = new List<MaterialBackup>();
            public List<ComponentBackup> components = new List<ComponentBackup>();
            public List<TextureBackup> textures = new List<TextureBackup>();
        }

        /// <summary>
        /// Creates a backup of the avatar state
        /// </summary>
        public static string CreateBackup(GameObject avatarRoot, string backupLocation)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string backupFolder = Path.Combine(backupLocation, "VRCQuestPatcher", timestamp);
                
                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                }

                BackupData backup = new BackupData
                {
                    timestamp = timestamp,
                    avatarRootPath = GetGameObjectPath(avatarRoot)
                };

                // Backup materials
                EditorUtility.DisplayProgressBar("Creating Backup", "Backing up materials...", 0.3f);
                backup.materials = BackupMaterials(avatarRoot);

                // Backup components
                EditorUtility.DisplayProgressBar("Creating Backup", "Backing up components...", 0.6f);
                backup.components = BackupComponents(avatarRoot);

                // Backup textures
                EditorUtility.DisplayProgressBar("Creating Backup", "Backing up textures...", 0.9f);
                backup.textures = BackupTextures(avatarRoot);

                // Save backup
                string backupPath = Path.Combine(backupFolder, "backup.json");
                string json = JsonUtility.ToJson(backup, true);
                File.WriteAllText(backupPath, json);

                EditorUtility.ClearProgressBar();
                Debug.Log($"Backup created at: {backupPath}");
                return backupPath;
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"Error creating backup: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Backs up all materials used in the avatar
        /// </summary>
        private static List<MaterialBackup> BackupMaterials(GameObject avatarRoot)
        {
            List<MaterialBackup> backups = new List<MaterialBackup>();
            HashSet<Material> processedMaterials = new HashSet<Material>();

            Renderer[] renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;

                Material[] materials = renderer.sharedMaterials;
                foreach (Material mat in materials)
                {
                    if (mat == null || processedMaterials.Contains(mat))
                        continue;

                    processedMaterials.Add(mat);

                    string materialPath = AssetDatabase.GetAssetPath(mat);
                    if (string.IsNullOrEmpty(materialPath))
                        continue; // Skip runtime materials

                    MaterialBackup backup = new MaterialBackup
                    {
                        materialPath = materialPath,
                        shaderName = mat.shader != null ? mat.shader.name : "None"
                    };

                    // Backup material properties
                    backup.materialProperties = new List<MaterialPropertyEntry>();
                    if (mat.shader != null)
                    {
                        #if UNITY_2021_2_OR_NEWER
                        int propertyCount = UnityEditor.ShaderUtil.GetPropertyCount(mat.shader);
                        for (int i = 0; i < propertyCount; i++)
                        {
                            string propertyName = UnityEditor.ShaderUtil.GetPropertyName(mat.shader, i);
                            UnityEditor.ShaderUtil.ShaderPropertyType propertyType = UnityEditor.ShaderUtil.GetPropertyType(mat.shader, i);
                            
                            try
                            {
                                // Skip properties that don't exist or can't be accessed
                                if (!mat.HasProperty(propertyName))
                                    continue;
                                
                                MaterialPropertyEntry entry = new MaterialPropertyEntry
                                {
                                    propertyName = propertyName
                                };
                                
                                switch (propertyType)
                                {
                                    case UnityEditor.ShaderUtil.ShaderPropertyType.Color:
                                        try
                                        {
                                            entry.propertyType = "color";
                                            entry.propertyValue = mat.GetColor(propertyName).ToString();
                                            backup.materialProperties.Add(entry);
                                        }
                                        catch
                                        {
                                            // Skip properties that can't be read (e.g., enum-based color properties)
                                        }
                                        break;
                                    case UnityEditor.ShaderUtil.ShaderPropertyType.Vector:
                                        try
                                        {
                                            entry.propertyType = "vector";
                                            entry.propertyValue = mat.GetVector(propertyName).ToString();
                                            backup.materialProperties.Add(entry);
                                        }
                                        catch
                                        {
                                            // Skip properties that can't be read
                                        }
                                        break;
                                    case UnityEditor.ShaderUtil.ShaderPropertyType.Float:
                                    case UnityEditor.ShaderUtil.ShaderPropertyType.Range:
                                        try
                                        {
                                            // Note: GetFloat on enum properties may generate MaterialEnum warnings
                                            // These are harmless Unity internal warnings and can be ignored
                                            entry.propertyType = "float";
                                            entry.propertyValue = mat.GetFloat(propertyName).ToString();
                                            backup.materialProperties.Add(entry);
                                        }
                                        catch
                                        {
                                            // Skip enum properties that cause MaterialEnum errors
                                            // These warnings don't affect functionality
                                        }
                                        break;
                                    case UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv:
                                        try
                                        {
                                            Texture tex = mat.GetTexture(propertyName);
                                            if (tex != null)
                                            {
                                                entry.propertyType = "texture";
                                                entry.propertyValue = AssetDatabase.GetAssetPath(tex);
                                                backup.materialProperties.Add(entry);
                                            }
                                        }
                                        catch
                                        {
                                            // Skip texture properties that can't be read
                                        }
                                        break;
                                }
                            }
                            catch
                            {
                                // Skip properties that can't be read
                            }
                        }
                        #else
                        // For older Unity versions, backup common properties manually
                        string[] commonProperties = { "_Color", "_MainTex", "_BumpMap", "_EmissionColor", "_EmissionMap" };
                        foreach (string propName in commonProperties)
                        {
                            try
                            {
                                if (mat.HasProperty(propName))
                                {
                                    MaterialPropertyEntry entry = new MaterialPropertyEntry
                                    {
                                        propertyName = propName
                                    };
                                    
                                    if (propName.Contains("Tex") || propName.Contains("Map"))
                                    {
                                        Texture tex = mat.GetTexture(propName);
                                        if (tex != null)
                                        {
                                            entry.propertyType = "texture";
                                            entry.propertyValue = AssetDatabase.GetAssetPath(tex);
                                            backup.materialProperties.Add(entry);
                                        }
                                    }
                                    else if (propName.Contains("Color"))
                                    {
                                        entry.propertyType = "color";
                                        entry.propertyValue = mat.GetColor(propName).ToString();
                                        backup.materialProperties.Add(entry);
                                    }
                                    else
                                    {
                                        entry.propertyType = "float";
                                        entry.propertyValue = mat.GetFloat(propName).ToString();
                                        backup.materialProperties.Add(entry);
                                    }
                                }
                            }
                            catch
                            {
                                // Skip properties that can't be read
                            }
                        }
                        #endif
                    }

                    backups.Add(backup);
                }
            }

            return backups;
        }

        /// <summary>
        /// Backs up components that will be removed
        /// </summary>
        private static List<ComponentBackup> BackupComponents(GameObject avatarRoot)
        {
            List<ComponentBackup> backups = new List<ComponentBackup>();
            HashSet<Component> processedComponents = new HashSet<Component>();

            Component[] allComponents = avatarRoot.GetComponentsInChildren<Component>(true);
            foreach (Component comp in allComponents)
            {
                if (comp == null || processedComponents.Contains(comp))
                    continue;

                string componentType = comp.GetType().FullName;
                
                // Only backup components that will be removed
                if (IsQuestIncompatibleComponent(comp))
                {
                    processedComponents.Add(comp);
                    
                    ComponentBackup backup = new ComponentBackup
                    {
                        gameObjectPath = GetGameObjectPath(comp.gameObject),
                        componentType = componentType
                    };

                    // Try to serialize component data using EditorJsonUtility
                    try
                    {
                        SerializedObject so = new SerializedObject(comp);
                        backup.componentData = EditorJsonUtility.ToJson(so, true);
                        
                        // If EditorJsonUtility fails or returns empty, try manual serialization
                        if (string.IsNullOrEmpty(backup.componentData) || backup.componentData == "{}")
                        {
                            backup.componentData = SerializeComponentManually(so);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Failed to serialize component {componentType} on {backup.gameObjectPath}: {e.Message}");
                        backup.componentData = "{}";
                    }

                    backups.Add(backup);
                }
            }

            return backups;
        }

        /// <summary>
        /// Backs up texture import settings
        /// </summary>
        private static List<TextureBackup> BackupTextures(GameObject avatarRoot)
        {
            List<TextureBackup> backups = new List<TextureBackup>();
            HashSet<Texture> processedTextures = new HashSet<Texture>();

            Renderer[] renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;

                Material[] materials = renderer.sharedMaterials;
                foreach (Material mat in materials)
                {
                    if (mat == null || mat.shader == null) continue;

                    #if UNITY_2021_2_OR_NEWER
                    int propertyCount = UnityEditor.ShaderUtil.GetPropertyCount(mat.shader);
                    for (int i = 0; i < propertyCount; i++)
                    {
                        if (UnityEditor.ShaderUtil.GetPropertyType(mat.shader, i) == UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv)
                        {
                            string propertyName = UnityEditor.ShaderUtil.GetPropertyName(mat.shader, i);
                    #else
                    // For older Unity versions, use reflection or skip texture backup
                    string[] propertyNames = { "_MainTex", "_BumpMap", "_EmissionMap", "_DetailAlbedoMap", "_DetailNormalMap" };
                    foreach (string propertyName in propertyNames)
                    {
                        if (mat.HasProperty(propertyName))
                        {
                    #endif
                            Texture tex = mat.GetTexture(propertyName);
                            
                            if (tex != null && !processedTextures.Contains(tex))
                            {
                                processedTextures.Add(tex);
                                string texturePath = AssetDatabase.GetAssetPath(tex);
                                
                                if (!string.IsNullOrEmpty(texturePath))
                                {
                                    TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                                    if (importer != null)
                                    {
                                        TextureBackup backup = new TextureBackup
                                        {
                                            texturePath = texturePath,
                                            maxTextureSize = importer.maxTextureSize,
                                            compression = importer.textureCompression
                                        };

                                        // Get platform settings for Android
                                        #if UNITY_2018_1_OR_NEWER
                                        TextureImporterPlatformSettings androidSettings = importer.GetPlatformTextureSettings("Android");
                                        if (androidSettings != null)
                                        {
                                            backup.format = androidSettings.format;
                                            backup.useCrunchCompression = androidSettings.crunchedCompression;
                                            backup.compressorQuality = androidSettings.compressionQuality;
                                        }
                                        #endif

                                        backups.Add(backup);
                                    }
                                }
                            }
                        }
                    #if UNITY_2021_2_OR_NEWER
                    }
                    #else
                    }
                    #endif
                }
            }

            return backups;
        }

        /// <summary>
        /// Restores avatar from backup
        /// </summary>
        public static bool RestoreFromBackup(string backupPath)
        {
            try
            {
                if (!File.Exists(backupPath))
                {
                    Debug.LogError($"Backup file not found: {backupPath}");
                    return false;
                }

                string json = File.ReadAllText(backupPath);
                BackupData backup = JsonUtility.FromJson<BackupData>(json);

                if (backup == null)
                {
                    Debug.LogError("Failed to parse backup file");
                    return false;
                }

                EditorUtility.DisplayProgressBar("Restoring Backup", "Restoring materials...", 0.3f);
                RestoreMaterials(backup.materials);

                EditorUtility.DisplayProgressBar("Restoring Backup", "Restoring textures...", 0.6f);
                RestoreTextures(backup.textures);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.ClearProgressBar();
                Debug.Log("Backup restored successfully");
                return true;
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"Error restoring backup: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        private static void RestoreMaterials(List<MaterialBackup> materials)
        {
            foreach (MaterialBackup backup in materials)
            {
                try
                {
                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(backup.materialPath);
                    if (mat != null)
                    {
                        Shader shader = Shader.Find(backup.shaderName);
                        if (shader != null)
                        {
                            mat.shader = shader;
                            
                            // Restore properties if possible
                            if (backup.materialProperties != null && backup.materialProperties.Count > 0)
                            {
                                foreach (var entry in backup.materialProperties)
                                {
                                    try
                                    {
                                        if (mat.HasProperty(entry.propertyName))
                                        {
                                            switch (entry.propertyType)
                                            {
                                                case "float":
                                                    if (float.TryParse(entry.propertyValue, out float floatValue))
                                                        mat.SetFloat(entry.propertyName, floatValue);
                                                    break;
                                                case "color":
                                                    if (TryParseColor(entry.propertyValue, out Color colorValue))
                                                        mat.SetColor(entry.propertyName, colorValue);
                                                    break;
                                                case "vector":
                                                    if (TryParseVector(entry.propertyValue, out Vector4 vectorValue))
                                                        mat.SetVector(entry.propertyName, vectorValue);
                                                    break;
                                                case "texture":
                                                    Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(entry.propertyValue);
                                                    if (tex != null)
                                                        mat.SetTexture(entry.propertyName, tex);
                                                    break;
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        // Skip properties that can't be restored
                                    }
                                }
                            }

                            EditorUtility.SetDirty(mat);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to restore material {backup.materialPath}: {e.Message}");
                }
            }
        }

        private static void RestoreTextures(List<TextureBackup> textures)
        {
            foreach (TextureBackup backup in textures)
            {
                try
                {
                    TextureImporter importer = AssetImporter.GetAtPath(backup.texturePath) as TextureImporter;
                    if (importer != null)
                    {
                        importer.maxTextureSize = backup.maxTextureSize;
                        importer.textureCompression = backup.compression;

                        #if UNITY_2018_1_OR_NEWER
                        TextureImporterPlatformSettings platformSettings = new TextureImporterPlatformSettings();
                        platformSettings.name = "Android";
                        platformSettings.maxTextureSize = backup.maxTextureSize;
                        platformSettings.format = backup.format;
                        platformSettings.compressionQuality = backup.compressorQuality;
                        platformSettings.crunchedCompression = backup.useCrunchCompression;
                        importer.SetPlatformTextureSettings(platformSettings);
                        #endif

                        importer.SaveAndReimport();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to restore texture {backup.texturePath}: {e.Message}");
                }
            }
        }


        private static bool IsQuestIncompatibleComponent(Component comp)
        {
            if (comp == null) return false;
            
            string typeName = comp.GetType().FullName;
            string typeNameLower = typeName.ToLowerInvariant();
            
            return typeNameLower.Contains("dynamicbone") ||
                   comp is Cloth ||
                   comp is Camera ||
                   comp is Light ||
                   comp is AudioSource ||
                   comp is Rigidbody ||
                   comp is Collider ||
                   comp is Joint ||
                   comp is ParticleSystem ||
                   typeNameLower.Contains("constraint") ||
                   typeNameLower.Contains("finalik");
        }
    }
}
