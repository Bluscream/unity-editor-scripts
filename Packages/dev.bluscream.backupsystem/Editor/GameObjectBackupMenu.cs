using UnityEditor;
using UnityEngine;

namespace Bluscream.BackupSystem
{
    /// <summary>
    /// Adds context menu items to GameObjects for quick backup operations
    /// </summary>
    public static class GameObjectBackupMenu
    {
        [MenuItem("GameObject/Backup/Backup GameObject", false, 0)]
        public static void BackupGameObject(MenuCommand command)
        {
            GameObject target = command.context as GameObject;
            if (target == null)
            {
                // Fallback to selection if context is not available
                if (Selection.activeGameObject != null)
                {
                    target = Selection.activeGameObject;
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "No GameObject selected.", "OK");
                    return;
                }
            }

            CreateBackupForGameObject(target, BackupScope.SingleGameObject);
        }

        [MenuItem("GameObject/Backup/Backup Recursively", false, 1)]
        public static void BackupGameObjectRecursively(MenuCommand command)
        {
            GameObject target = command.context as GameObject;
            if (target == null)
            {
                // Fallback to selection if context is not available
                if (Selection.activeGameObject != null)
                {
                    target = Selection.activeGameObject;
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "No GameObject selected.", "OK");
                    return;
                }
            }

            CreateBackupForGameObject(target, BackupScope.GameObjectRecursive);
        }

        [MenuItem("GameObject/Backup/Backup GameObject", true)]
        [MenuItem("GameObject/Backup/Backup Recursively", true)]
        public static bool ValidateBackupMenu()
        {
            // Only show menu items when a GameObject is selected
            return Selection.activeGameObject != null;
        }

        private static void CreateBackupForGameObject(GameObject target, BackupScope scope)
        {
            if (target == null)
            {
                EditorUtility.DisplayDialog("Error", "No GameObject selected.", "OK");
                return;
            }

            try
            {
                BackupConfig config = new BackupConfig
                {
                    backupMaterials = true,
                    backupComponents = true,
                    backupTextures = true,
                    backupGameObjectHierarchy = true,
                    includeMaterialProperties = true,
                    includeComponentData = true,
                    backupLocation = "Assets/Backups",
                    backupName = $"{target.name}_{scope}"
                };

                EditorUtility.DisplayProgressBar("Creating Backup", $"Backing up {target.name}...", 0f);

                string backupPath = BackupSystem.CreateBackup(
                    config,
                    scope,
                    target,
                    (message, progress) =>
                    {
                        EditorUtility.DisplayProgressBar("Creating Backup", message, progress);
                    }
                );

                EditorUtility.ClearProgressBar();

                if (backupPath != null)
                {
                    EditorUtility.DisplayDialog(
                        "Backup Complete",
                        $"Backup created successfully!\n\nLocation: {backupPath}",
                        "OK"
                    );
                    Debug.Log($"Backup created for {target.name}: {backupPath}");
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "Backup Failed",
                        "Failed to create backup. Check console for details.",
                        "OK"
                    );
                }
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog(
                    "Error",
                    $"Error creating backup: {e.Message}",
                    "OK"
                );
                Debug.LogError($"Error creating backup: {e.Message}\n{e.StackTrace}");
            }
        }
    }
}
