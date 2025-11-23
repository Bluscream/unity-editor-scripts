using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRCQuestPatcher
{
    /// <summary>
    /// Optimizes textures for Quest by applying compression
    /// </summary>
    public static class TextureOptimizer
    {
        public class OptimizedTexture
        {
            public string texturePath;
            public int originalSize;
            public int newSize;
        }

        /// <summary>
        /// Optimizes all textures used by the avatar
        /// </summary>
        public static List<OptimizedTexture> OptimizeTextures(
            GameObject avatarRoot, 
            int maxTextureSize = 1024,
            int compressionQuality = 75,
            bool useCrunchCompression = true,
            System.Action<string> progressCallback = null)
        {
            List<OptimizedTexture> optimized = new List<OptimizedTexture>();
            HashSet<string> processedTextures = new HashSet<string>();

            // Find all textures used in materials
            Renderer[] renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            List<string> texturePaths = new List<string>();

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;

                Material[] materials = renderer.sharedMaterials;
                foreach (Material mat in materials)
                {
                    if (mat == null || mat.shader == null) continue;

                    CollectTexturePaths(mat, texturePaths, processedTextures);
                }
            }

            int total = texturePaths.Count;
            for (int i = 0; i < texturePaths.Count; i++)
            {
                string texturePath = texturePaths[i];
                progressCallback?.Invoke($"Optimizing textures ({i + 1}/{total}): {System.IO.Path.GetFileName(texturePath)}");

                try
                {
                    TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                    if (importer != null)
                    {
                        OptimizedTexture opt = OptimizeTexture(importer, texturePath, maxTextureSize, compressionQuality, useCrunchCompression);
                        if (opt != null)
                        {
                            optimized.Add(opt);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to optimize texture {texturePath}: {e.Message}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return optimized;
        }

        /// <summary>
        /// Optimizes a single texture
        /// </summary>
        private static OptimizedTexture OptimizeTexture(
            TextureImporter importer, 
            string texturePath,
            int maxTextureSize,
            int compressionQuality,
            bool useCrunchCompression)
        {
            try
            {
                // Get original size
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                int originalSize = tex != null ? (tex.width * tex.height) : 0;

                // Apply compression settings
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.maxTextureSize = maxTextureSize;

                // Set Android platform settings
                #if UNITY_2018_1_OR_NEWER
                TextureImporterPlatformSettings androidSettings = importer.GetPlatformTextureSettings("Android");
                if (androidSettings == null)
                {
                    androidSettings = new TextureImporterPlatformSettings();
                    androidSettings.name = "Android";
                }

                androidSettings.maxTextureSize = maxTextureSize;
                androidSettings.format = TextureImporterFormat.Automatic;
                androidSettings.compressionQuality = compressionQuality;
                androidSettings.crunchedCompression = useCrunchCompression;
                androidSettings.textureCompression = TextureImporterCompression.Compressed;

                importer.SetPlatformTextureSettings(androidSettings);
                #else
                // Fallback for older Unity versions
                importer.SetPlatformTextureSettings(
                    "Android",
                    maxTextureSize,
                    TextureImporterFormat.Automatic,
                    compressionQuality,
                    useCrunchCompression
                );
                #endif

                importer.SaveAndReimport();

                // Get new size
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                int newSize = tex != null ? (tex.width * tex.height) : 0;

                return new OptimizedTexture
                {
                    texturePath = texturePath,
                    originalSize = originalSize,
                    newSize = newSize
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Error optimizing texture {texturePath}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Collects all texture paths from a material
        /// </summary>
        private static void CollectTexturePaths(Material mat, List<string> texturePaths, HashSet<string> processed)
        {
            if (mat == null || mat.shader == null) return;

            #if UNITY_2021_2_OR_NEWER
            int propertyCount = UnityEditor.ShaderUtil.GetPropertyCount(mat.shader);
            for (int i = 0; i < propertyCount; i++)
            {
                if (UnityEditor.ShaderUtil.GetPropertyType(mat.shader, i) == UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    string propertyName = UnityEditor.ShaderUtil.GetPropertyName(mat.shader, i);
            #else
            // For older Unity versions, check common texture properties
            string[] commonTextureProperties = { "_MainTex", "_BumpMap", "_EmissionMap", "_DetailAlbedoMap", "_DetailNormalMap", "_MetallicGlossMap", "_OcclusionMap" };
            foreach (string propertyName in commonTextureProperties)
            {
                if (mat.HasProperty(propertyName))
                {
            #endif
                    Texture tex = mat.GetTexture(propertyName);

                    if (tex != null)
                    {
                        string texturePath = AssetDatabase.GetAssetPath(tex);
                        if (!string.IsNullOrEmpty(texturePath) && !processed.Contains(texturePath))
                        {
                            processed.Add(texturePath);
                            texturePaths.Add(texturePath);
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
