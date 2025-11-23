using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Bluscream.BackupSystem
{
    /// <summary>
    /// Core backup system for Unity assets and GameObjects
    /// </summary>
    public static class BackupSystem
    {
        /// <summary>
        /// Creates a backup based on the configuration
        /// </summary>
        public static string CreateBackup(BackupConfig config, BackupScope scope, GameObject targetGameObject = null, System.Action<string, float> progressCallback = null)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string backupFolderName = string.IsNullOrEmpty(config.backupName) ? timestamp : $"{config.backupName}_{timestamp}";
                string backupFolder = Path.Combine(config.backupLocation, backupFolderName);
                
                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                }

                BackupData backup = new BackupData
                {
                    timestamp = timestamp,
                    backupName = config.backupName,
                    scope = scope,
                    targetPath = scope == BackupScope.AllAssets ? "ALL_ASSETS" : (targetGameObject != null ? Utils.GetGameObjectPath(targetGameObject) : "")
                };

                float progress = 0f;
                float progressStep = 1f / 4f;

                // Backup materials
                if (config.backupMaterials)
                {
                    progressCallback?.Invoke("Backing up materials...", progress);
                    backup.materials = MaterialBackupHandler.BackupMaterials(scope, targetGameObject, config.includeMaterialProperties);
                    progress += progressStep;
                }

                // Backup components
                if (config.backupComponents)
                {
                    progressCallback?.Invoke("Backing up components...", progress);
                    backup.components = ComponentBackupHandler.BackupComponents(scope, targetGameObject, config.includeComponentData);
                    progress += progressStep;
                }

                // Backup textures
                if (config.backupTextures)
                {
                    progressCallback?.Invoke("Backing up textures...", progress);
                    backup.textures = TextureBackupHandler.BackupTextures(scope, targetGameObject);
                    progress += progressStep;
                }

                // Backup GameObject hierarchy
                if (config.backupGameObjectHierarchy && scope != BackupScope.AllAssets && targetGameObject != null)
                {
                    progressCallback?.Invoke("Backing up GameObject hierarchy...", progress);
                    backup.gameObjects = GameObjectBackupHandler.BackupGameObjectHierarchy(targetGameObject, scope == BackupScope.GameObjectRecursive);
                    progress += progressStep;
                }

                // Save backup as separate JSON files (always create files, even if empty)
                if (config.backupMaterials)
                {
                    string materialsPath = Path.Combine(backupFolder, "materials.json");
                    if (backup.materials == null) backup.materials = new System.Collections.Generic.List<MaterialBackup>();
                    MaterialsWrapper materialsWrapper = new MaterialsWrapper { materials = backup.materials };
                    string materialsJson = JsonUtility.ToJson(materialsWrapper, true);
                    File.WriteAllText(materialsPath, materialsJson);
                    Debug.Log($"Saved {backup.materials.Count} materials to {materialsPath}");
                }

                if (config.backupComponents)
                {
                    string componentsPath = Path.Combine(backupFolder, "components.json");
                    if (backup.components == null) backup.components = new System.Collections.Generic.List<ComponentBackup>();
                    ComponentsWrapper componentsWrapper = new ComponentsWrapper { components = backup.components };
                    string componentsJson = JsonUtility.ToJson(componentsWrapper, true);
                    File.WriteAllText(componentsPath, componentsJson);
                    Debug.Log($"Saved {backup.components.Count} components to {componentsPath}");
                }

                if (config.backupTextures)
                {
                    string texturesPath = Path.Combine(backupFolder, "textures.json");
                    if (backup.textures == null) backup.textures = new System.Collections.Generic.List<TextureBackup>();
                    TexturesWrapper texturesWrapper = new TexturesWrapper { textures = backup.textures };
                    string texturesJson = JsonUtility.ToJson(texturesWrapper, true);
                    File.WriteAllText(texturesPath, texturesJson);
                    Debug.Log($"Saved {backup.textures.Count} textures to {texturesPath}");
                }

                if (config.backupGameObjectHierarchy)
                {
                    string hierarchyPath = Path.Combine(backupFolder, "hierarchy.json");
                    if (backup.gameObjects == null) backup.gameObjects = new System.Collections.Generic.List<GameObjectBackup>();
                    HierarchyWrapper hierarchyWrapper = new HierarchyWrapper { gameObjects = backup.gameObjects };
                    string hierarchyJson = JsonUtility.ToJson(hierarchyWrapper, true);
                    File.WriteAllText(hierarchyPath, hierarchyJson);
                    Debug.Log($"Saved {backup.gameObjects.Count} gameObjects to {hierarchyPath}");
                }

                // Backup asset information to CSV
                progressCallback?.Invoke("Backing up asset information...", 0.9f);
                string assetsCsvPath = Path.Combine(backupFolder, "assets.csv");
                int assetCount = 0;
                try
                {
                    AssetBackupHandler.BackupAssetsToCsv(scope, targetGameObject, assetsCsvPath, (message, progress) =>
                    {
                        progressCallback?.Invoke(message, 0.9f + (progress * 0.1f));
                    });
                    
                    // Count lines in CSV (excluding header)
                    if (File.Exists(assetsCsvPath))
                    {
                        string[] lines = File.ReadAllLines(assetsCsvPath);
                        assetCount = lines.Length > 1 ? lines.Length - 1 : 0; // Subtract header
                        Debug.Log($"Saved {assetCount} assets to {assetsCsvPath}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to create assets.csv: {e.Message}");
                }

                // Save comprehensive metadata file
                string metadataPath = Path.Combine(backupFolder, "backup.json");
                BackupMetadata metadata = BackupMetadata.Create(
                    config,
                    scope,
                    backupFolder,
                    backup.targetPath
                );
                string metadataJson = JsonUtility.ToJson(metadata, true);
                File.WriteAllText(metadataPath, metadataJson);

                // Log backup summary with counts
                System.Text.StringBuilder summary = new System.Text.StringBuilder();
                summary.AppendLine($"Backup created at: {backupFolder}");
                summary.Append("Summary: ");
                List<string> parts = new List<string>();
                if (config.backupMaterials && backup.materials != null)
                    parts.Add($"{backup.materials.Count} materials");
                if (config.backupComponents && backup.components != null)
                    parts.Add($"{backup.components.Count} components");
                if (config.backupTextures && backup.textures != null)
                    parts.Add($"{backup.textures.Count} textures");
                if (config.backupGameObjectHierarchy && backup.gameObjects != null)
                    parts.Add($"{backup.gameObjects.Count} gameObjects");
                if (assetCount > 0)
                    parts.Add($"{assetCount} assets");
                summary.Append(string.Join(", ", parts));
                
                progressCallback?.Invoke("Backup complete!", 1f);
                Debug.Log(summary.ToString());
                return backupFolder;
            }
            catch (Exception e)
            {
                progressCallback?.Invoke("Backup failed!", 1f);
                Debug.LogError($"Error creating backup: {e.Message}\n{e.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Loads metadata from a backup folder
        /// </summary>
        public static BackupMetadata LoadMetadata(string backupPath)
        {
            try
            {
                string backupFolder = Directory.Exists(backupPath) ? backupPath : Path.GetDirectoryName(backupPath);
                string metadataPath = Path.Combine(backupFolder, "backup.json");
                
                if (!File.Exists(metadataPath))
                {
                    Debug.LogWarning($"Metadata file not found: {metadataPath}");
                    return null;
                }

                string metadataJson = File.ReadAllText(metadataPath);
                BackupMetadata metadata = JsonUtility.FromJson<BackupMetadata>(metadataJson);
                return metadata;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load backup metadata: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Restores from backup with selective restoration
        /// </summary>
        public static bool RestoreFromBackup(string backupPath, BackupConfig restoreConfig, System.Action<string, float> progressCallback = null)
        {
            try
            {
                // backupPath should be a directory containing the backup files
                string backupFolder = Directory.Exists(backupPath) ? backupPath : Path.GetDirectoryName(backupPath);
                
                if (!Directory.Exists(backupFolder))
                {
                    Debug.LogError($"Backup folder not found: {backupFolder}");
                    return false;
                }

                // Load backup data from separate files
                BackupData backup = new BackupData();
                
                // Load materials
                string materialsPath = Path.Combine(backupFolder, "materials.json");
                if (File.Exists(materialsPath) && restoreConfig.backupMaterials)
                {
                    try
                    {
                        string materialsJson = File.ReadAllText(materialsPath);
                        var materialsWrapper = JsonUtility.FromJson<MaterialsWrapper>(materialsJson);
                        if (materialsWrapper != null && materialsWrapper.materials != null)
                        {
                            backup.materials = materialsWrapper.materials;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Failed to load materials.json: {e.Message}");
                    }
                }

                // Load components
                string componentsPath = Path.Combine(backupFolder, "components.json");
                if (File.Exists(componentsPath) && restoreConfig.backupComponents)
                {
                    try
                    {
                        string componentsJson = File.ReadAllText(componentsPath);
                        var componentsWrapper = JsonUtility.FromJson<ComponentsWrapper>(componentsJson);
                        if (componentsWrapper != null && componentsWrapper.components != null)
                        {
                            backup.components = componentsWrapper.components;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Failed to load components.json: {e.Message}");
                    }
                }

                // Load textures
                string texturesPath = Path.Combine(backupFolder, "textures.json");
                if (File.Exists(texturesPath) && restoreConfig.backupTextures)
                {
                    try
                    {
                        string texturesJson = File.ReadAllText(texturesPath);
                        var texturesWrapper = JsonUtility.FromJson<TexturesWrapper>(texturesJson);
                        if (texturesWrapper != null && texturesWrapper.textures != null)
                        {
                            backup.textures = texturesWrapper.textures;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Failed to load textures.json: {e.Message}");
                    }
                }

                // Load hierarchy
                string hierarchyPath = Path.Combine(backupFolder, "hierarchy.json");
                if (File.Exists(hierarchyPath) && restoreConfig.backupGameObjectHierarchy)
                {
                    try
                    {
                        string hierarchyJson = File.ReadAllText(hierarchyPath);
                        var hierarchyWrapper = JsonUtility.FromJson<HierarchyWrapper>(hierarchyJson);
                        if (hierarchyWrapper != null && hierarchyWrapper.gameObjects != null)
                        {
                            backup.gameObjects = hierarchyWrapper.gameObjects;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Failed to load hierarchy.json: {e.Message}");
                    }
                }

                return RestoreFromBackupData(backup, restoreConfig, progressCallback);
            }
            catch (System.Exception e)
            {
                progressCallback?.Invoke("Restore failed!", 1f);
                Debug.LogError($"Error restoring backup: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        // Wrapper classes for JSON deserialization
        [System.Serializable]
        private class MaterialsWrapper
        {
            public System.Collections.Generic.List<MaterialBackup> materials;
        }

        [System.Serializable]
        private class ComponentsWrapper
        {
            public System.Collections.Generic.List<ComponentBackup> components;
        }

        [System.Serializable]
        private class TexturesWrapper
        {
            public System.Collections.Generic.List<TextureBackup> textures;
        }

        [System.Serializable]
        private class HierarchyWrapper
        {
            public System.Collections.Generic.List<GameObjectBackup> gameObjects;
        }

        /// <summary>
        /// Internal method to restore from BackupData
        /// </summary>
        private static bool RestoreFromBackupData(BackupData backup, BackupConfig restoreConfig, System.Action<string, float> progressCallback)
        {
            try
            {
                if (backup == null)
                {
                    Debug.LogError("Backup data is null");
                    return false;
                }

                float progress = 0f;
                float progressStep = 1f / 3f;

                if (restoreConfig.backupMaterials)
                {
                    progressCallback?.Invoke("Restoring materials...", progress);
                    MaterialBackupHandler.RestoreMaterials(backup.materials, restoreConfig.includeMaterialProperties);
                    progress += progressStep;
                }

                if (restoreConfig.backupTextures)
                {
                    progressCallback?.Invoke("Restoring textures...", progress);
                    TextureBackupHandler.RestoreTextures(backup.textures);
                    progress += progressStep;
                }

                if (restoreConfig.backupComponents && restoreConfig.backupGameObjectHierarchy)
                {
                    progressCallback?.Invoke("Restoring GameObjects and components...", progress);
                    GameObjectBackupHandler.RestoreGameObjects(backup.gameObjects, backup.components, restoreConfig.includeComponentData);
                    progress += progressStep;
                }
                else if (restoreConfig.backupComponents)
                {
                    progressCallback?.Invoke("Restoring components...", progress);
                    ComponentBackupHandler.RestoreComponents(backup.components, restoreConfig.includeComponentData);
                    progress += progressStep;
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                progressCallback?.Invoke("Restore complete!", 1f);
                Debug.Log("Backup restored successfully");
                return true;
            }
            catch (Exception e)
            {
                progressCallback?.Invoke("Restore failed!", 1f);
                Debug.LogError($"Error restoring backup: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }
    }
}
