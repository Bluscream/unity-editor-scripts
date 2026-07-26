using System;
using UnityEditor;
using UnityEngine;

namespace Bluscream.Replacer
{
    /// <summary>
    /// Stores the source GameObject or prefab for replacement
    /// </summary>
    public static class GameObjectReplacer
    {
        private static GameObject sourceGameObject;
        private static GameObject sourcePrefab;
        private static string sourceName;

        /// <summary>
        /// Sets the source GameObject from the scene
        /// </summary>
        public static void SetSourceGameObject(GameObject go)
        {
            if (go == null)
            {
                sourceGameObject = null;
                sourcePrefab = null;
                sourceName = null;
                return;
            }

            sourceGameObject = go;
            sourcePrefab = null;
            sourceName = go.name;
            
            Debug.Log($"Source GameObject set: {sourceName}");
        }

        /// <summary>
        /// Sets the source prefab asset
        /// </summary>
        public static void SetSourcePrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                sourceGameObject = null;
                sourcePrefab = null;
                sourceName = null;
                return;
            }

            sourcePrefab = prefab;
            sourceGameObject = null;
            sourceName = prefab.name;
            
            Debug.Log($"Source Prefab set: {sourceName}");
        }

        /// <summary>
        /// Gets the current source name
        /// </summary>
        public static string GetSourceName()
        {
            return sourceName;
        }

        /// <summary>
        /// Checks if a source is set
        /// </summary>
        public static bool HasSource()
        {
            return sourceGameObject != null || sourcePrefab != null;
        }

        /// <summary>
        /// Recursively copies all children from source transform to destination transform
        /// </summary>
        private static void CopyChildrenRecursive(Transform sourceParent, Transform destParent)
        {
            // Iterate through all children of the source
            for (int i = 0; i < sourceParent.childCount; i++)
            {
                Transform sourceChild = sourceParent.GetChild(i);
                
                // Skip inactive children if needed, or copy all regardless
                // For now, we'll copy all children
                
                // Duplicate the child GameObject
                GameObject duplicatedChild = UnityEngine.Object.Instantiate(sourceChild.gameObject);
                
                // Register undo for the new child
                Undo.RegisterCreatedObjectUndo(duplicatedChild, "Replace GameObject - Copy Children");
                
                // Set parent to destination
                duplicatedChild.transform.SetParent(destParent, false);
                
                // Preserve local transform properties
                duplicatedChild.transform.localPosition = sourceChild.localPosition;
                duplicatedChild.transform.localRotation = sourceChild.localRotation;
                duplicatedChild.transform.localScale = sourceChild.localScale;
                
                // Preserve name
                duplicatedChild.name = sourceChild.name;
                
                // Recursively copy children of this child
                CopyChildrenRecursive(sourceChild, duplicatedChild.transform);
            }
        }

        /// <summary>
        /// Replaces the target GameObject with the source
        /// </summary>
        public static void ReplaceGameObject(GameObject target)
        {
            if (target == null)
            {
                Debug.LogError("Cannot replace: Target GameObject is null");
                return;
            }

            if (!HasSource())
            {
                Debug.LogError("Cannot replace: No source GameObject or prefab set. Right-click on a GameObject or prefab and select 'Replace with ...' first.");
                EditorUtility.DisplayDialog("No Source", "No source GameObject or prefab set.\n\nRight-click on a GameObject or prefab and select 'Replace with ...' first.", "OK");
                return;
            }

            try
            {
                // Store target information
                string targetName = target.name;
                Transform targetParent = target.transform.parent;
                Vector3 targetPosition = target.transform.localPosition;
                Quaternion targetRotation = target.transform.localRotation;
                Vector3 targetScale = target.transform.localScale;

                // Rename and disable target
                Undo.RegisterCompleteObjectUndo(target, "Replace GameObject");
                target.name = $"{targetName} (replaced)";
                target.SetActive(false);

                // Get or create source instance
                GameObject sourceInstance = null;

                if (sourcePrefab != null)
                {
                    // Instantiate prefab
                    sourceInstance = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
                    if (sourceInstance == null)
                    {
                        Debug.LogError($"Failed to instantiate prefab: {sourcePrefab.name}");
                        return;
                    }
                }
                else if (sourceGameObject != null)
                {
                    // Check if source is in scene or is a prefab instance
                    bool isPrefabInstance = Utils.IsPrefabInstance(sourceGameObject);
                    bool isPrefabAsset = Utils.IsPrefabAsset(sourceGameObject);

                    if (isPrefabAsset)
                    {
                        // It's a prefab asset, instantiate it
                        sourceInstance = PrefabUtility.InstantiatePrefab(sourceGameObject) as GameObject;
                    }
                    else if (isPrefabInstance)
                    {
                        // It's a prefab instance, instantiate the prefab asset
                        GameObject prefabAsset = Utils.GetPrefabAsset(sourceGameObject);
                        if (prefabAsset != null)
                        {
                            sourceInstance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
                        }
                        else
                        {
                            // Fallback: duplicate the instance
                            sourceInstance = UnityEngine.Object.Instantiate(sourceGameObject);
                        }
                    }
                    else
                    {
                        // It's a regular scene GameObject, duplicate it
                        sourceInstance = UnityEngine.Object.Instantiate(sourceGameObject);
                    }
                }

                if (sourceInstance == null)
                {
                    Debug.LogError("Failed to create source instance");
                    return;
                }

                // Register undo for the new instance
                Undo.RegisterCreatedObjectUndo(sourceInstance, "Replace GameObject");

                // Move source to target's parent
                sourceInstance.transform.SetParent(targetParent, false);

                // Apply target's transform properties
                sourceInstance.transform.localPosition = targetPosition;
                sourceInstance.transform.localRotation = targetRotation;
                sourceInstance.transform.localScale = targetScale;

                // Rename source to target's original name
                sourceInstance.name = targetName;

                // Copy all child GameObjects from target to source instance
                CopyChildrenRecursive(target.transform, sourceInstance.transform);

                // Select the new instance
                Selection.activeGameObject = sourceInstance;

                Debug.Log($"Replaced '{targetName}' with '{sourceName}'");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error replacing GameObject: {e.Message}\n{e.StackTrace}");
                EditorUtility.DisplayDialog("Replace Error", $"Error replacing GameObject:\n{e.Message}", "OK");
            }
        }
    }
}
