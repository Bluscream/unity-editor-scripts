using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bluscream.Cleanup
{
    /// <summary>
    /// Information about an asset that will be deleted
    /// </summary>
    [System.Serializable]
    public class AssetDeletionInfo
    {
        public string assetPath;
        public string reason;
        public long sizeInBytes;
        public string assetType;
    }

    /// <summary>
    /// Core asset cleanup functionality
    /// </summary>
    public static class AssetCleanup
    {
        /// <summary>
        /// Analyzes assets in the specified folder and returns a list of unused assets
        /// </summary>
        public static List<AssetDeletionInfo> AnalyzeUnusedAssets(string folderPath, bool recursive, System.Action<string, float> progressCallback = null)
        {
            List<AssetDeletionInfo> unusedAssets = new List<AssetDeletionInfo>();
            
            progressCallback?.Invoke("Collecting assets...", 0f);
            
            // Get all assets in the folder
            HashSet<string> assetsInFolder = new HashSet<string>();
            if (recursive)
            {
                string[] guids = AssetDatabase.FindAssets("", new[] { folderPath });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path) && path.StartsWith(folderPath))
                    {
                        assetsInFolder.Add(path);
                    }
                }
            }
            else
            {
                // Non-recursive: only assets directly in the folder
                string[] guids = AssetDatabase.FindAssets("", new[] { folderPath });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path) && 
                        path.StartsWith(folderPath) && 
                        !path.Substring(folderPath.Length + 1).Contains("/"))
                    {
                        assetsInFolder.Add(path);
                    }
                }
            }

            progressCallback?.Invoke("Analyzing asset usage...", 0.3f);
            
            // Get all used assets
            HashSet<string> usedAssets = GetUsedAssets(progressCallback);
            
            progressCallback?.Invoke("Identifying unused assets...", 0.7f);
            
            // Find unused assets
            int total = assetsInFolder.Count;
            int current = 0;
            foreach (string assetPath in assetsInFolder)
            {
                current++;
                if (current % 50 == 0)
                {
                    progressCallback?.Invoke($"Analyzing assets... ({current}/{total})", 0.7f + (current / (float)total) * 0.3f);
                }

                // Skip .meta files
                if (assetPath.EndsWith(".meta"))
                    continue;

                // Check if asset is used
                if (!usedAssets.Contains(assetPath))
                {
                    string reason = GetDeletionReason(assetPath, usedAssets);
                    if (!string.IsNullOrEmpty(reason))
                    {
                        AssetDeletionInfo info = new AssetDeletionInfo
                        {
                            assetPath = assetPath,
                            reason = reason,
                            sizeInBytes = GetAssetSize(assetPath),
                            assetType = GetAssetType(assetPath)
                        };
                        unusedAssets.Add(info);
                    }
                }
            }

            return unusedAssets;
        }

        /// <summary>
        /// Gets all assets that are currently in use
        /// </summary>
        private static HashSet<string> GetUsedAssets(System.Action<string, float> progressCallback = null)
        {
            HashSet<string> usedAssets = new HashSet<string>();
            
            // 1. Assets in current scene
            progressCallback?.Invoke("Scanning current scene...", 0.1f);
            Scene currentScene = SceneManager.GetActiveScene();
            if (currentScene.isLoaded)
            {
                GameObject[] rootObjects = currentScene.GetRootGameObjects();
                foreach (GameObject root in rootObjects)
                {
                    CollectUsedAssetsFromGameObject(root, usedAssets, true);
                }
            }

            // 2. Resources folder assets (always considered used)
            progressCallback?.Invoke("Scanning Resources...", 0.2f);
            string[] resourceGuids = AssetDatabase.FindAssets("", new[] { "Assets/Resources" });
            foreach (string guid in resourceGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && !path.EndsWith(".meta"))
                {
                    usedAssets.Add(path);
                }
            }

            // 3. Editor scripts and editor-only assets
            progressCallback?.Invoke("Scanning Editor assets...", 0.3f);
            string[] editorGuids = AssetDatabase.FindAssets("", new[] { "Assets/Editor" });
            foreach (string guid in editorGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && !path.EndsWith(".meta"))
                {
                    usedAssets.Add(path);
                }
            }

            // 4. Plugins folder
            progressCallback?.Invoke("Scanning Plugins...", 0.4f);
            string[] pluginGuids = AssetDatabase.FindAssets("", new[] { "Assets/Plugins" });
            foreach (string guid in pluginGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && !path.EndsWith(".meta"))
                {
                    usedAssets.Add(path);
                }
            }

            // 5. StreamingAssets folder
            progressCallback?.Invoke("Scanning StreamingAssets...", 0.5f);
            string[] streamingGuids = AssetDatabase.FindAssets("", new[] { "Assets/StreamingAssets" });
            foreach (string guid in streamingGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && !path.EndsWith(".meta"))
                {
                    usedAssets.Add(path);
                }
            }

            // 6. Prefabs in Resources (runtime instantiated)
            progressCallback?.Invoke("Scanning prefabs...", 0.6f);
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && !path.EndsWith(".meta"))
                {
                    // Check if prefab is referenced in scene or Resources
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null)
                    {
                        // Check if prefab is in Resources or referenced in scene
                        if (path.Contains("/Resources/") || IsPrefabReferencedInScene(prefab))
                        {
                            usedAssets.Add(path);
                            CollectUsedAssetsFromGameObject(prefab, usedAssets, true);
                        }
                    }
                }
            }

            // 7. ScriptableObjects that might be used at runtime
            progressCallback?.Invoke("Scanning ScriptableObjects...", 0.7f);
            string[] soGuids = AssetDatabase.FindAssets("t:ScriptableObject");
            foreach (string guid in soGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && !path.EndsWith(".meta"))
                {
                    if (path.Contains("/Resources/") || IsScriptableObjectReferenced(path, usedAssets))
                    {
                        usedAssets.Add(path);
                    }
                }
            }

            // 8. Addressables (if available)
            progressCallback?.Invoke("Scanning Addressables...", 0.8f);
            try
            {
                // Check if Addressables package is available
                System.Type addressableAssetType = System.Type.GetType("UnityEngine.AddressableAssets.AddressableAssetSettings, Unity.Addressables");
                if (addressableAssetType != null)
                {
                    string[] addressableGuids = AssetDatabase.FindAssets("", new[] { "Assets" });
                    foreach (string guid in addressableGuids)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (!string.IsNullOrEmpty(path) && !path.EndsWith(".meta"))
                        {
                            // Check if asset is marked as addressable
                            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                            if (obj != null)
                            {
                                // This is a simplified check - full Addressables integration would be more complex
                                if (path.Contains("AddressableAssets") || IsAddressableAsset(path))
                                {
                                    usedAssets.Add(path);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return usedAssets;
        }

        /// <summary>
        /// Collects all assets referenced by a GameObject and its children
        /// </summary>
        private static void CollectUsedAssetsFromGameObject(GameObject go, HashSet<string> usedAssets, bool recursive)
        {
            if (go == null) return;

            // Get all components
            Component[] components = recursive 
                ? go.GetComponentsInChildren<Component>(true)
                : go.GetComponents<Component>();

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
                        if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/"))
                        {
                            usedAssets.Add(path);
                            
                            // If it's a GameObject/Prefab, also collect its assets
                            if (prop.objectReferenceValue is GameObject refGo)
                            {
                                CollectUsedAssetsFromGameObject(refGo, usedAssets, true);
                            }
                        }
                    }
                }
            }

            // Check if GameObject itself is a prefab
            string prefabPath = AssetDatabase.GetAssetPath(go);
            if (!string.IsNullOrEmpty(prefabPath) && prefabPath.StartsWith("Assets/"))
            {
                usedAssets.Add(prefabPath);
            }
        }

        /// <summary>
        /// Checks if a prefab is referenced in the current scene
        /// </summary>
        private static bool IsPrefabReferencedInScene(GameObject prefab)
        {
            Scene currentScene = SceneManager.GetActiveScene();
            if (!currentScene.isLoaded) return false;

            GameObject[] rootObjects = currentScene.GetRootGameObjects();
            foreach (GameObject root in rootObjects)
            {
                if (IsPrefabInstance(root, prefab))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if a GameObject is an instance of a prefab
        /// </summary>
        private static bool IsPrefabInstance(GameObject go, GameObject prefab)
        {
            #if UNITY_2018_3_OR_NEWER
            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                GameObject prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (prefabAsset == prefab)
                    return true;
            }
            #else
            // For Unity versions before 2018.3
            try
            {
                #if UNITY_5_0 || UNITY_5_1 || UNITY_5_2 || UNITY_5_3 || UNITY_5_4 || UNITY_5_5 || UNITY_5_6 || UNITY_2017_1 || UNITY_2017_2
                PrefabType prefabType = PrefabUtility.GetPrefabType(go);
                if (prefabType == PrefabType.PrefabInstance)
                {
                    GameObject prefabAsset = PrefabUtility.GetPrefabParent(go) as GameObject;
                    if (prefabAsset == prefab)
                        return true;
                }
                #else
                // Unity 2018.1, 2018.2 - try alternative method
                GameObject prefabAsset = PrefabUtility.GetPrefabParent(go) as GameObject;
                if (prefabAsset == prefab)
                    return true;
                #endif
            }
            catch
            {
                // Fallback for any version issues
                GameObject prefabAsset = PrefabUtility.GetPrefabParent(go) as GameObject;
                if (prefabAsset == prefab)
                    return true;
            }
            #endif

            // Check children
            foreach (Transform child in go.transform)
            {
                if (IsPrefabInstance(child.gameObject, prefab))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if a ScriptableObject is referenced
        /// </summary>
        private static bool IsScriptableObjectReferenced(string soPath, HashSet<string> usedAssets)
        {
            // Check if referenced in any asset
            string[] allGuids = AssetDatabase.FindAssets("t:Script");
            foreach (string guid in allGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains(soPath))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if an asset is marked as Addressable
        /// </summary>
        private static bool IsAddressableAsset(string assetPath)
        {
            // Simplified check - full implementation would use Addressables API
            return false;
        }

        /// <summary>
        /// Gets the reason why an asset can be deleted
        /// </summary>
        private static string GetDeletionReason(string assetPath, HashSet<string> usedAssets)
        {
            // Check if asset is in a protected folder
            if (assetPath.Contains("/Editor/") || 
                assetPath.Contains("/Resources/") || 
                assetPath.Contains("/Plugins/") || 
                assetPath.Contains("/StreamingAssets/") ||
                assetPath.Contains("/Gizmos/"))
            {
                return ""; // Don't delete protected assets
            }

            // Check if it's a script
            if (assetPath.EndsWith(".cs") || assetPath.EndsWith(".js"))
            {
                return ""; // Don't delete scripts
            }

            // Check if it's a shader
            if (assetPath.EndsWith(".shader") || assetPath.EndsWith(".compute"))
            {
                return ""; // Don't delete shaders
            }

            // Check if it's referenced by any used asset
            string[] dependencies = AssetDatabase.GetDependencies(assetPath, false);
            foreach (string dep in dependencies)
            {
                if (usedAssets.Contains(dep) && dep != assetPath)
                {
                    return ""; // Don't delete if referenced by used asset
                }
            }

            // Check reverse dependencies (what depends on this asset)
            string[] reverseDeps = AssetDatabase.GetDependencies(new[] { assetPath }, false);
            foreach (string revDep in reverseDeps)
            {
                if (usedAssets.Contains(revDep) && revDep != assetPath)
                {
                    return ""; // Don't delete if something used depends on it
                }
            }

            // Asset is not used
            return "Not referenced in current scene or protected folders";
        }

        /// <summary>
        /// Gets the file size of an asset
        /// </summary>
        private static long GetAssetSize(string assetPath)
        {
            try
            {
                string projectRoot = Application.dataPath.Replace("Assets", "").Replace("\\Assets", "").Replace("/Assets", "");
                string fullPath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath))
                {
                    FileInfo fileInfo = new FileInfo(fullPath);
                    return fileInfo.Length;
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Gets the type of an asset
        /// </summary>
        private static string GetAssetType(string assetPath)
        {
            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (obj != null)
            {
                return obj.GetType().Name;
            }
            return "Unknown";
        }

        /// <summary>
        /// Deletes the specified assets
        /// </summary>
        public static void DeleteAssets(List<AssetDeletionInfo> assetsToDelete, System.Action<string, float> progressCallback = null)
        {
            int total = assetsToDelete.Count;
            for (int i = 0; i < total; i++)
            {
                AssetDeletionInfo info = assetsToDelete[i];
                progressCallback?.Invoke($"Deleting {info.assetPath}...", (float)i / total);

                try
                {
                    AssetDatabase.DeleteAsset(info.assetPath);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to delete {info.assetPath}: {e.Message}");
                }
            }

            AssetDatabase.Refresh();
        }
    }
}
