using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bluscream.BackupSystem
{
    /// <summary>
    /// Log handler that filters out Unity's material drawer warnings
    /// </summary>
    internal class MaterialBackupLogHandler : ILogHandler
    {
        private ILogHandler defaultHandler;
        
        public MaterialBackupLogHandler()
        {
            defaultHandler = Debug.unityLogger.logHandler;
        }
        
        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            // Filter out "Failed to create material drawer" warnings
            if (logType == LogType.Warning)
            {
                string message = string.Format(format, args);
                if (message.Contains("Failed to create material drawer"))
                {
                    return; // Suppress this specific warning
                }
            }
            
            defaultHandler.LogFormat(logType, context, format, args);
        }
        
        public void LogException(Exception exception, UnityEngine.Object context)
        {
            defaultHandler.LogException(exception, context);
        }
    }

    /// <summary>
    /// Material property entry for backup
    /// </summary>
    [System.Serializable]
    public class MaterialPropertyEntry
    {
        public string propertyName;
        public string propertyType; // "float", "color", "vector", "texture"
        public string propertyValue;
    }

    /// <summary>
    /// Material backup data structure
    /// </summary>
    [System.Serializable]
    public class MaterialBackup
    {
        public string materialPath;
        public string shaderName;
        public List<MaterialPropertyEntry> materialProperties = new List<MaterialPropertyEntry>();
    }

    /// <summary>
    /// Handles material and shader backup operations
    /// </summary>
    public static class MaterialBackupHandler
    {
        /// <summary>
        /// Backs up materials based on scope
        /// </summary>
        public static List<MaterialBackup> BackupMaterials(BackupScope scope, GameObject targetGameObject, bool includeProperties)
        {
            List<MaterialBackup> backups = new List<MaterialBackup>();
            HashSet<Material> processedMaterials = new HashSet<Material>();

            if (scope == BackupScope.AllAssets)
            {
                // Backup all materials in the project
                string[] materialGuids = AssetDatabase.FindAssets("t:Material");
                foreach (string guid in materialGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat != null && !processedMaterials.Contains(mat))
                    {
                        processedMaterials.Add(mat);
                        backups.Add(CreateMaterialBackup(mat, includeProperties));
                    }
                }
            }
            else if (targetGameObject != null)
            {
                // Backup materials from GameObject(s)
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
                        if (mat != null && !processedMaterials.Contains(mat))
                        {
                            processedMaterials.Add(mat);
                            string materialPath = AssetDatabase.GetAssetPath(mat);
                            if (!string.IsNullOrEmpty(materialPath))
                            {
                                backups.Add(CreateMaterialBackup(mat, includeProperties));
                            }
                        }
                    }
                }
            }

            return backups;
        }

        /// <summary>
        /// Creates a material backup entry
        /// </summary>
        private static MaterialBackup CreateMaterialBackup(Material mat, bool includeProperties)
        {
            MaterialBackup backup = new MaterialBackup
            {
                materialPath = AssetDatabase.GetAssetPath(mat),
                shaderName = mat.shader != null ? mat.shader.name : "None"
            };

            if (includeProperties && mat.shader != null)
            {
                backup.materialProperties = BackupMaterialProperties(mat);
            }

            return backup;
        }

        /// <summary>
        /// Backs up material properties
        /// </summary>
        private static List<MaterialPropertyEntry> BackupMaterialProperties(Material mat)
        {
            List<MaterialPropertyEntry> properties = new List<MaterialPropertyEntry>();

            #if UNITY_2021_2_OR_NEWER
            // Temporarily suppress Unity's "Failed to create material drawer" warnings
            // These warnings come from Unity's internal material property drawer system and don't affect functionality
            ILogHandler originalLogHandler = Debug.unityLogger.logHandler;
            MaterialBackupLogHandler filterHandler = new MaterialBackupLogHandler();
            Debug.unityLogger.logHandler = filterHandler;
            
            try
            {
                int propertyCount = UnityEditor.ShaderUtil.GetPropertyCount(mat.shader);
                for (int i = 0; i < propertyCount; i++)
                {
                    string propertyName = UnityEditor.ShaderUtil.GetPropertyName(mat.shader, i);
                    UnityEditor.ShaderUtil.ShaderPropertyType propertyType = UnityEditor.ShaderUtil.GetPropertyType(mat.shader, i);
                    
                    // Skip HasProperty check to avoid triggering material drawer initialization
                    // We know the property exists from ShaderUtil, so just try to get its value directly
                    // Errors from custom material drawers (like ThryRGBAPacker) are caught and ignored
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
                                properties.Add(entry);
                            }
                            catch { }
                            break;
                        case UnityEditor.ShaderUtil.ShaderPropertyType.Vector:
                            try
                            {
                                entry.propertyType = "vector";
                                entry.propertyValue = mat.GetVector(propertyName).ToString();
                                properties.Add(entry);
                            }
                            catch { }
                            break;
                        case UnityEditor.ShaderUtil.ShaderPropertyType.Float:
                        case UnityEditor.ShaderUtil.ShaderPropertyType.Range:
                            try
                            {
                                entry.propertyType = "float";
                                entry.propertyValue = mat.GetFloat(propertyName).ToString();
                                properties.Add(entry);
                            }
                            catch { }
                            break;
                        case UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv:
                            try
                            {
                                Texture tex = mat.GetTexture(propertyName);
                                if (tex != null)
                                {
                                    entry.propertyType = "texture";
                                    entry.propertyValue = AssetDatabase.GetAssetPath(tex);
                                    properties.Add(entry);
                                }
                            }
                            catch { }
                            break;
                    }
                }
            }
            finally
            {
                // Restore original logging behavior
                Debug.unityLogger.logHandler = originalLogHandler;
            }
            #else
            // For older Unity versions, backup common properties
            // Temporarily suppress Unity's "Failed to create material drawer" warnings
            ILogHandler originalLogHandler = Debug.unityLogger.logHandler;
            MaterialBackupLogHandler filterHandler = new MaterialBackupLogHandler();
            Debug.unityLogger.logHandler = filterHandler;
            
            try
            {
                // Skip HasProperty check to avoid triggering material drawer initialization
                // Errors from custom material drawers are caught and ignored
                string[] commonProperties = { "_Color", "_MainTex", "_BumpMap", "_EmissionColor", "_EmissionMap" };
                foreach (string propName in commonProperties)
                {
                    try
                    {
                        MaterialPropertyEntry entry = new MaterialPropertyEntry { propertyName = propName };
                        
                        if (propName.Contains("Tex") || propName.Contains("Map"))
                        {
                            try
                            {
                                Texture tex = mat.GetTexture(propName);
                                if (tex != null)
                                {
                                    entry.propertyType = "texture";
                                    entry.propertyValue = AssetDatabase.GetAssetPath(tex);
                                    properties.Add(entry);
                                }
                            }
                            catch { }
                        }
                        else if (propName.Contains("Color"))
                        {
                            try
                            {
                                entry.propertyType = "color";
                                entry.propertyValue = mat.GetColor(propName).ToString();
                                properties.Add(entry);
                            }
                            catch { }
                        }
                        else
                        {
                            try
                            {
                                entry.propertyType = "float";
                                entry.propertyValue = mat.GetFloat(propName).ToString();
                                properties.Add(entry);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            finally
            {
                // Restore original logging behavior
                Debug.unityLogger.logHandler = originalLogHandler;
            }
            #endif

            return properties;
        }

        /// <summary>
        /// Restores materials from backup
        /// </summary>
        public static void RestoreMaterials(List<MaterialBackup> materials, bool includeProperties)
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
                            
                            if (includeProperties && backup.materialProperties != null && backup.materialProperties.Count > 0)
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
                                                    if (Utils.TryParseColor(entry.propertyValue, out Color colorValue))
                                                        mat.SetColor(entry.propertyName, colorValue);
                                                    break;
                                                case "vector":
                                                    if (Utils.TryParseVector(entry.propertyValue, out Vector4 vectorValue))
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
                                    catch { }
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
    }
}
