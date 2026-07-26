using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bluscream.BackupSystem
{
    /// <summary>
    /// Represents a single property entry in component data
    /// </summary>
    [System.Serializable]
    public class ComponentPropertyEntry
    {
        public string key;           // Property name
        public string value;        // Property value as string
        public string type;         // Property type (e.g., "Integer", "String", "Vector3", etc.)
        
        public ComponentPropertyEntry() { }
        
        public ComponentPropertyEntry(string key, string value, string type)
        {
            this.key = key;
            this.value = value;
            this.type = type;
        }
    }

    /// <summary>
    /// Component backup data structure
    /// </summary>
    [System.Serializable]
    public class ComponentBackup
    {
        public string gameObjectPath;
        public string componentType;
        
        // New format: Dictionary-like structure using List<ComponentPropertyEntry>
        public List<ComponentPropertyEntry> componentData;
        
        // Legacy format: JSON string (for backward compatibility)
        [System.Obsolete("Use componentData instead. This field is kept for backward compatibility.")]
        public string componentDataString;
        
        /// <summary>
        /// Gets component data as a dictionary-like structure for easy access
        /// </summary>
        public Dictionary<string, ComponentPropertyEntry> GetDataAsDictionary()
        {
            Dictionary<string, ComponentPropertyEntry> dict = new Dictionary<string, ComponentPropertyEntry>();
            if (componentData != null)
            {
                foreach (var entry in componentData)
                {
                    if (!string.IsNullOrEmpty(entry.key))
                    {
                        dict[entry.key] = entry;
                    }
                }
            }
            return dict;
        }
    }

    /// <summary>
    /// Handles component backup operations
    /// </summary>
    public static class ComponentBackupHandler
    {
        /// <summary>
        /// Backs up components based on scope
        /// </summary>
        public static List<ComponentBackup> BackupComponents(BackupScope scope, GameObject targetGameObject, bool includeData)
        {
            List<ComponentBackup> backups = new List<ComponentBackup>();
            HashSet<Component> processedComponents = new HashSet<Component>();

            if (scope == BackupScope.AllAssets)
            {
                // For all assets, we can't easily backup all components
                // This would require finding all prefabs and scene objects
                // For now, skip this scope for components
                return backups;
            }
            else if (targetGameObject != null)
            {
                bool recursive = scope == BackupScope.GameObjectRecursive;
                Component[] components = recursive
                    ? targetGameObject.GetComponentsInChildren<Component>(true)
                    : targetGameObject.GetComponents<Component>();

                foreach (Component comp in components)
                {
                    if (comp == null || processedComponents.Contains(comp))
                        continue;

                    processedComponents.Add(comp);

                    ComponentBackup backup = new ComponentBackup
                    {
                        gameObjectPath = Utils.GetGameObjectPath(comp.gameObject),
                        componentType = comp.GetType().FullName
                    };

                    if (includeData)
                    {
                        try
                        {
                            SerializedObject so = new SerializedObject(comp);
                            // Use the new dictionary-like structure
                            backup.componentData = Utils.SerializeComponentToPropertyList(so);
                            
                            // Fallback to string format if list is empty (for compatibility)
                            if (backup.componentData == null || backup.componentData.Count == 0)
                            {
                                string jsonData = EditorJsonUtility.ToJson(so, true);
                                if (string.IsNullOrEmpty(jsonData) || jsonData == "{}")
                                {
                                    jsonData = Utils.SerializeComponentManually(so);
                                }
                                // If we got JSON data, try to parse it or store as legacy format
                                if (!string.IsNullOrEmpty(jsonData) && jsonData != "{}")
                                {
                                    // Keep componentData as empty list, store JSON in legacy field for backward compatibility
                                    backup.componentData = new List<ComponentPropertyEntry>();
                                    #pragma warning disable CS0618 // Type or member is obsolete
                                    backup.componentDataString = jsonData;
                                    #pragma warning restore CS0618
                                }
                                else
                                {
                                    backup.componentData = new List<ComponentPropertyEntry>();
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"Failed to serialize component {backup.componentType}: {e.Message}");
                            backup.componentData = new List<ComponentPropertyEntry>();
                        }
                    }
                    else
                    {
                        backup.componentData = new List<ComponentPropertyEntry>();
                    }

                    backups.Add(backup);
                }
            }

            return backups;
        }

        /// <summary>
        /// Restores components from backup
        /// </summary>
        public static void RestoreComponents(List<ComponentBackup> components, bool includeData)
        {
            // Component restoration would require finding GameObjects by path
            // This is complex and may not always work if hierarchy changed
            // For now, just log a warning
            Debug.LogWarning("Component restoration is not fully implemented. GameObjects may need to be restored first.");
        }
    }
}
