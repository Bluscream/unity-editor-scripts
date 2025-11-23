using System;
using UnityEditor;
using UnityEngine;
using static Bluscream.Utils;

namespace VRCQuestPatcher
{
    /// <summary>
    /// Helper class to check if BackupSystem package is available and use it if possible
    /// </summary>
    public static class BackupSystemHelper
    {

        /// <summary>
        /// Creates a backup using BackupSystem if available, otherwise falls back to BackupManager
        /// </summary>
        public static string CreateBackup(GameObject avatarRoot, string backupLocation, System.Action<string, float> progressCallback = null)
        {
            if (IsBackupSystemAvailable())
            {
                try
                {
                    // Use BackupSystem
                    System.Type backupSystemType = System.Type.GetType("Bluscream.BackupSystem.BackupSystem, Assembly-CSharp-Editor") 
                        ?? System.Type.GetType("Bluscream.BackupSystem.BackupSystem");
                    
                    if (backupSystemType != null)
                    {
                        System.Type configType = System.Type.GetType("Bluscream.BackupSystem.BackupConfig, Assembly-CSharp-Editor")
                            ?? System.Type.GetType("Bluscream.BackupSystem.BackupConfig");
                        
                        if (configType != null)
                        {
                            object config = Activator.CreateInstance(configType);
                            configType.GetProperty("backupMaterials").SetValue(config, true);
                            configType.GetProperty("backupComponents").SetValue(config, true);
                            configType.GetProperty("backupTextures").SetValue(config, true);
                            configType.GetProperty("backupGameObjectHierarchy").SetValue(config, false);
                            configType.GetProperty("includeMaterialProperties").SetValue(config, true);
                            configType.GetProperty("includeComponentData").SetValue(config, true);
                            configType.GetProperty("backupLocation").SetValue(config, backupLocation);
                            configType.GetProperty("backupName").SetValue(config, "QuestPatcher");

                            System.Type scopeType = System.Type.GetType("Bluscream.BackupSystem.BackupScope, Assembly-CSharp-Editor")
                                ?? System.Type.GetType("Bluscream.BackupSystem.BackupScope");
                            
                            if (scopeType != null)
                            {
                                object scope = Enum.Parse(scopeType, "GameObjectRecursive");
                                
                                var createBackupMethod = backupSystemType.GetMethod("CreateBackup", 
                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                                
                                if (createBackupMethod != null)
                                {
                                    object result = createBackupMethod.Invoke(null, new object[] { config, scope, avatarRoot, progressCallback });
                                    return result as string;
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to use BackupSystem, falling back to BackupManager: {e.Message}");
                }
            }

            // Fallback to BackupManager
            return BackupManager.CreateBackup(avatarRoot, backupLocation);
        }

        /// <summary>
        /// Shows a confirmation dialog if BackupSystem is not available
        /// </summary>
        public static bool ConfirmWithoutBackupSystem()
        {
            if (IsBackupSystemAvailable())
                return true; // No need to confirm if BackupSystem is available

            return EditorUtility.DisplayDialog(
                "Backup System Not Available",
                "The BackupSystem package (dev.bluscream.backupsystem) is not installed.\n\n" +
                "Proceeding without a backup system means:\n" +
                "• No automatic backup will be created\n" +
                "• You cannot restore changes if something goes wrong\n" +
                "• Changes will be permanent\n\n" +
                "It is HIGHLY RECOMMENDED to install the BackupSystem package before proceeding.\n\n" +
                "Do you want to continue anyway?",
                "Yes, Continue Without Backup",
                "Cancel"
            );
        }
    }
}
