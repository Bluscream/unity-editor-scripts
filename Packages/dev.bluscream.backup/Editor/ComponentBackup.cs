using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Bluscream;
using ComponentPropertyEntry = Bluscream.ComponentPropertyEntry;

namespace Bluscream.BackupSystem
{
    /// <summary>
    /// Component backup data structure
    /// </summary>
    [System.Serializable]
    public class ComponentBackup
    {
        public string gameObjectPath;
        public string componentType;
        
        public List<ComponentPropertyEntry> componentData;
        
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
                            backup.componentData = Utils.SerializeComponentToPropertyList(so);
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
