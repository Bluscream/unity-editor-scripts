using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Bluscream.BackupSystem
{
    /// <summary>
    /// Handles asset information backup (CSV format with path, size, MD5)
    /// </summary>
    public static class AssetBackupHandler
    {
        /// <summary>
        /// Collects asset information based on scope and writes to CSV
        /// </summary>
        public static void BackupAssetsToCsv(BackupScope scope, GameObject targetGameObject, string outputPath, System.Action<string, float> progressCallback = null)
        {
            HashSet<string> assetPaths = new HashSet<string>();

            if (scope == BackupScope.AllAssets)
            {
                // Collect all assets in the project
                progressCallback?.Invoke("Collecting all assets...", 0f);
                string[] allGuids = AssetDatabase.FindAssets("");
                foreach (string guid in allGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path) && 
                        path.StartsWith("Assets/") && 
                        !path.StartsWith("Assets/Backups/") &&
                        !path.Contains("/Library/") &&
                        !path.Contains("/Temp/"))
                    {
                        assetPaths.Add(path);
                    }
                }
            }
            else if (targetGameObject != null)
            {
                // Collect assets referenced by GameObject(s)
                progressCallback?.Invoke("Collecting referenced assets...", 0f);
                bool recursive = scope == BackupScope.GameObjectRecursive;
                
                // Get all renderers to find materials and textures
                Renderer[] renderers = recursive
                    ? targetGameObject.GetComponentsInChildren<Renderer>(true)
                    : targetGameObject.GetComponents<Renderer>();

                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null) continue;

                    // Materials and their textures
                    Material[] materials = renderer.sharedMaterials;
                    foreach (Material mat in materials)
                    {
                        if (mat != null)
                        {
                            string path = AssetDatabase.GetAssetPath(mat);
                            if (!string.IsNullOrEmpty(path))
                                assetPaths.Add(path);
                            
                            // Get textures from material
                            #if UNITY_2021_2_OR_NEWER
                            int propertyCount = UnityEditor.ShaderUtil.GetPropertyCount(mat.shader);
                            for (int i = 0; i < propertyCount; i++)
                            {
                                UnityEditor.ShaderUtil.ShaderPropertyType propType = UnityEditor.ShaderUtil.GetPropertyType(mat.shader, i);
                                if (propType == UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv)
                                {
                                    string propName = UnityEditor.ShaderUtil.GetPropertyName(mat.shader, i);
                                    Texture tex = mat.GetTexture(propName);
                                    if (tex != null)
                                    {
                                        string texPath = AssetDatabase.GetAssetPath(tex);
                                        if (!string.IsNullOrEmpty(texPath))
                                            assetPaths.Add(texPath);
                                    }
                                }
                            }
                            #else
                            // For older Unity versions, check common texture properties
                            string[] commonTexProps = { "_MainTex", "_BumpMap", "_EmissionMap", "_DetailAlbedoMap", "_DetailNormalMap" };
                            foreach (string propName in commonTexProps)
                            {
                                try
                                {
                                    Texture tex = mat.GetTexture(propName);
                                    if (tex != null)
                                    {
                                        string texPath = AssetDatabase.GetAssetPath(tex);
                                        if (!string.IsNullOrEmpty(texPath))
                                            assetPaths.Add(texPath);
                                    }
                                }
                                catch { }
                            }
                            #endif
                        }
                    }

                    // Mesh
                    if (renderer is MeshRenderer meshRenderer)
                    {
                        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                        if (meshFilter != null && meshFilter.sharedMesh != null)
                        {
                            string path = AssetDatabase.GetAssetPath(meshFilter.sharedMesh);
                            if (!string.IsNullOrEmpty(path))
                                assetPaths.Add(path);
                        }
                    }
                    else if (renderer is SkinnedMeshRenderer skinnedMeshRenderer && skinnedMeshRenderer.sharedMesh != null)
                    {
                        string path = AssetDatabase.GetAssetPath(skinnedMeshRenderer.sharedMesh);
                        if (!string.IsNullOrEmpty(path))
                            assetPaths.Add(path);
                    }
                }

                // Get all components to find additional asset references
                Component[] components = recursive
                    ? targetGameObject.GetComponentsInChildren<Component>(true)
                    : targetGameObject.GetComponents<Component>();

                foreach (Component comp in components)
                {
                    if (comp == null) continue;

                    // Use SerializedObject to find all asset references
                    SerializedObject so = new SerializedObject(comp);
                    SerializedProperty prop = so.GetIterator();
                    while (prop.NextVisible(true))
                    {
                        if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue != null)
                        {
                            string path = AssetDatabase.GetAssetPath(prop.objectReferenceValue);
                            if (!string.IsNullOrEmpty(path) && !path.StartsWith("Library/"))
                                assetPaths.Add(path);
                        }
                    }
                }
            }

            // Write CSV file
            progressCallback?.Invoke("Writing assets.csv...", 0.5f);
            WriteAssetsCsv(assetPaths, outputPath, progressCallback);
        }

        /// <summary>
        /// Writes asset information to CSV file
        /// </summary>
        private static void WriteAssetsCsv(HashSet<string> assetPaths, string outputPath, System.Action<string, float> progressCallback = null)
        {
            using (StreamWriter writer = new StreamWriter(outputPath, false, Encoding.UTF8))
            {
                // Write header
                writer.WriteLine("asset path;size in bytes;md5");

                int total = assetPaths.Count;
                int current = 0;

                foreach (string assetPath in assetPaths)
                {
                    current++;
                    if (current % 100 == 0)
                    {
                        progressCallback?.Invoke($"Processing assets... ({current}/{total})", (float)current / total);
                    }

                    try
                    {
                        // Skip .meta files - we only want actual assets
                        if (assetPath.EndsWith(".meta"))
                            continue;

                        // Get full file path
                        // assetPath is relative to project root (e.g., "Assets/MyAsset.prefab")
                        // Application.dataPath is the full path to Assets folder
                        string projectRoot = Application.dataPath.Replace("Assets", "").Replace("\\Assets", "").Replace("/Assets", "");
                        string fullPath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
                        
                        if (!File.Exists(fullPath))
                        {
                            // Asset might not exist on disk (e.g., built-in assets)
                            continue;
                        }

                        // Get file size
                        FileInfo fileInfo = new FileInfo(fullPath);
                        long sizeInBytes = fileInfo.Length;

                        // Calculate MD5
                        string md5 = CalculateMD5(fullPath);
                        if (string.IsNullOrEmpty(md5))
                            continue; // Skip if MD5 calculation failed

                        // Write CSV line (escape semicolons in path)
                        string escapedPath = assetPath.Replace(";", "\\;");
                        writer.WriteLine($"{escapedPath};{sizeInBytes};{md5}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Failed to process asset {assetPath}: {e.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Calculates MD5 hash of a file
        /// </summary>
        private static string CalculateMD5(string filePath)
        {
            try
            {
                using (MD5 md5 = MD5.Create())
                {
                    using (FileStream stream = File.OpenRead(filePath))
                    {
                        byte[] hash = md5.ComputeHash(stream);
                        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to calculate MD5 for {filePath}: {e.Message}");
                return "";
            }
        }
    }
}
