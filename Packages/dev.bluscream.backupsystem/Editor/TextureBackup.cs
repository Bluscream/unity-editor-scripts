using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bluscream.BackupSystem
{
    /// <summary>
    /// Texture backup data structure
    /// </summary>
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

    /// <summary>
    /// Handles texture backup operations
    /// </summary>
    public static class TextureBackupHandler
    {
        /// <summary>
        /// Backs up textures based on scope
        /// </summary>
        public static List<TextureBackup> BackupTextures(BackupScope scope, GameObject targetGameObject)
        {
            List<TextureBackup> backups = new List<TextureBackup>();
            HashSet<Texture> processedTextures = new HashSet<Texture>();

            if (scope == BackupScope.AllAssets)
            {
                // Backup all textures in the project
                string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D");
                foreach (string guid in textureGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex != null && !processedTextures.Contains(tex))
                    {
                        processedTextures.Add(tex);
                        TextureBackup backup = CreateTextureBackup(path);
                        if (backup != null)
                            backups.Add(backup);
                    }
                }
            }
            else if (targetGameObject != null)
            {
                // Backup textures from materials used by GameObject(s)
                bool recursive = scope == BackupScope.GameObjectRecursive;
                Renderer[] renderers = recursive
                    ? targetGameObject.GetComponentsInChildren<Renderer>(true)
                    : targetGameObject.GetComponents<Renderer>();

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
                                        TextureBackup backup = CreateTextureBackup(texturePath);
                                        if (backup != null)
                                            backups.Add(backup);
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
            }

            return backups;
        }

        /// <summary>
        /// Creates a texture backup entry
        /// </summary>
        private static TextureBackup CreateTextureBackup(string texturePath)
        {
            try
            {
                TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                if (importer == null)
                    return null;

                TextureBackup backup = new TextureBackup
                {
                    texturePath = texturePath,
                    maxTextureSize = importer.maxTextureSize,
                    compression = importer.textureCompression
                };

                #if UNITY_2018_1_OR_NEWER
                TextureImporterPlatformSettings androidSettings = importer.GetPlatformTextureSettings("Android");
                if (androidSettings != null)
                {
                    backup.format = androidSettings.format;
                    backup.useCrunchCompression = androidSettings.crunchedCompression;
                    backup.compressorQuality = androidSettings.compressionQuality;
                }
                #endif

                return backup;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Restores textures from backup
        /// </summary>
        public static void RestoreTextures(List<TextureBackup> textures)
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
    }
}
