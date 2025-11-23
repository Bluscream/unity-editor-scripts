using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Bluscream.BackupSystem
{
    /// <summary>
    /// Editor window for the Backup System
    /// </summary>
    public class BackupWindow : EditorWindow
    {
        private BackupConfig backupConfig = new BackupConfig();
        private BackupConfig restoreConfig = new BackupConfig();
        private BackupScope backupScope = BackupScope.SingleGameObject;
        private GameObject targetGameObject;
        private string selectedBackupPath = "";
        private Vector2 scrollPosition;
        private bool isBackingUp = false;
        private bool isRestoring = false;
        private string progressMessage = "";
        private float progressValue = 0f;

        [MenuItem("Tools/Backup System")]
        public static void ShowWindow()
        {
            BackupWindow window = GetWindow<BackupWindow>("Backup System");
            window.minSize = new Vector2(500, 700);
            window.Show();
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Backup System", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Backup and restore Unity assets, GameObjects, and components with configurable options.", MessageType.Info);
            EditorGUILayout.Space(10);

            // Backup Section
            DrawBackupSection();

            EditorGUILayout.Space(20);

            // Restore Section
            DrawRestoreSection();

            EditorGUILayout.EndScrollView();

            // Handle progress updates
            if (isBackingUp || isRestoring)
            {
                Repaint();
            }
        }

        private void DrawBackupSection()
        {
            EditorGUILayout.LabelField("Backup", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Backup Scope
            EditorGUILayout.LabelField("Backup Scope", EditorStyles.boldLabel);
            backupScope = (BackupScope)EditorGUILayout.EnumPopup("Scope", backupScope);
            
            if (backupScope != BackupScope.AllAssets)
            {
                EditorGUILayout.Space(5);
                targetGameObject = EditorGUILayout.ObjectField("Target GameObject", targetGameObject, typeof(GameObject), true) as GameObject;
                
                if (backupScope == BackupScope.SingleGameObject)
                {
                    EditorGUILayout.HelpBox("Backs up only the selected GameObject (no children).", MessageType.Info);
                }
                else if (backupScope == BackupScope.GameObjectRecursive)
                {
                    EditorGUILayout.HelpBox("Backs up the selected GameObject and all its children recursively.", MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Backs up ALL assets in the project (materials, textures, etc.).", MessageType.Info);
            }

            EditorGUILayout.Space(10);

            // Backup Options
            EditorGUILayout.LabelField("What to Backup", EditorStyles.boldLabel);
            backupConfig.backupMaterials = EditorGUILayout.Toggle("Materials & Shaders", backupConfig.backupMaterials);
            if (backupConfig.backupMaterials)
            {
                EditorGUI.indentLevel++;
                backupConfig.includeMaterialProperties = EditorGUILayout.Toggle("Include Material Properties", backupConfig.includeMaterialProperties);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(5);
            backupConfig.backupComponents = EditorGUILayout.Toggle("Components", backupConfig.backupComponents);
            if (backupConfig.backupComponents)
            {
                EditorGUI.indentLevel++;
                backupConfig.includeComponentData = EditorGUILayout.Toggle("Include Component Data", backupConfig.includeComponentData);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(5);
            backupConfig.backupTextures = EditorGUILayout.Toggle("Textures", backupConfig.backupTextures);

            EditorGUILayout.Space(5);
            backupConfig.backupGameObjectHierarchy = EditorGUILayout.Toggle("GameObject Hierarchy", backupConfig.backupGameObjectHierarchy);
            EditorGUILayout.HelpBox("Saves GameObject positions, rotations, scales, and active states.", MessageType.None);

            EditorGUILayout.Space(10);

            // Backup Location
            EditorGUILayout.LabelField("Backup Settings", EditorStyles.boldLabel);
            backupConfig.backupLocation = EditorGUILayout.TextField("Backup Location", backupConfig.backupLocation);
            backupConfig.backupName = EditorGUILayout.TextField("Backup Name (optional)", backupConfig.backupName);
            EditorGUILayout.HelpBox("Leave backup name empty to use timestamp only.", MessageType.None);

            EditorGUILayout.Space(10);

            // Progress
            if (isBackingUp)
            {
                EditorGUILayout.LabelField("Progress", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(progressMessage);
                EditorGUI.ProgressBar(GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true)), progressValue, $"{progressValue * 100:F1}%");
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(10);
            }

            // Backup Button
            EditorGUI.BeginDisabledGroup(isBackingUp || isRestoring || (backupScope != BackupScope.AllAssets && targetGameObject == null));
            if (GUILayout.Button("Create Backup", GUILayout.Height(30)))
            {
                CreateBackup();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        private void DrawRestoreSection()
        {
            EditorGUILayout.LabelField("Restore", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Backup Folder Selection
            EditorGUILayout.LabelField("Backup Folder", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField("Backup Path", selectedBackupPath);
            if (GUILayout.Button("Browse...", GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Backup Folder", "Assets", "");
                if (!string.IsNullOrEmpty(path))
                {
                    // Convert to relative path if in Assets folder
                    if (path.StartsWith(Application.dataPath))
                    {
                        selectedBackupPath = "Assets" + path.Substring(Application.dataPath.Length);
                    }
                    else
                    {
                        selectedBackupPath = path;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // Display backup metadata if available
            if (!string.IsNullOrEmpty(selectedBackupPath) && IsValidBackupFolder(selectedBackupPath))
            {
                EditorGUILayout.Space(5);
                BackupMetadata metadata = BackupSystem.LoadMetadata(selectedBackupPath);
                if (metadata != null)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField("Backup Information", EditorStyles.boldLabel);
                    
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.TextField("Backup Name", metadata.backupName);
                    EditorGUILayout.TextField("Created", metadata.timestamp);
                    EditorGUILayout.TextField("Unity Version", metadata.unityVersion);
                    EditorGUILayout.TextField("Project", metadata.projectName);
                    EditorGUILayout.TextField("Scope", metadata.scope.ToString());
                    EditorGUILayout.TextField("Target", metadata.targetPath);
                    
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Backed Up:", EditorStyles.boldLabel);
                    EditorGUILayout.TextField("Materials", string.IsNullOrEmpty(metadata.backedUpMaterials) ? "Not backed up" : metadata.backedUpMaterials);
                    EditorGUILayout.TextField("Components", string.IsNullOrEmpty(metadata.backedUpComponents) ? "Not backed up" : metadata.backedUpComponents);
                    EditorGUILayout.TextField("Textures", string.IsNullOrEmpty(metadata.backedUpTextures) ? "Not backed up" : metadata.backedUpTextures);
                    EditorGUILayout.TextField("GameObject Hierarchy", string.IsNullOrEmpty(metadata.backedUpGameObjectHierarchy) ? "Not backed up" : metadata.backedUpGameObjectHierarchy);
                    EditorGUI.EndDisabledGroup();
                    
                    EditorGUILayout.EndVertical();
                }
            }

            EditorGUILayout.Space(10);

            // Restore Options
            EditorGUILayout.LabelField("What to Restore", EditorStyles.boldLabel);
            restoreConfig.backupMaterials = EditorGUILayout.Toggle("Materials & Shaders", restoreConfig.backupMaterials);
            if (restoreConfig.backupMaterials)
            {
                EditorGUI.indentLevel++;
                restoreConfig.includeMaterialProperties = EditorGUILayout.Toggle("Include Material Properties", restoreConfig.includeMaterialProperties);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(5);
            restoreConfig.backupComponents = EditorGUILayout.Toggle("Components", restoreConfig.backupComponents);
            if (restoreConfig.backupComponents)
            {
                EditorGUI.indentLevel++;
                restoreConfig.includeComponentData = EditorGUILayout.Toggle("Include Component Data", restoreConfig.includeComponentData);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(5);
            restoreConfig.backupTextures = EditorGUILayout.Toggle("Textures", restoreConfig.backupTextures);

            EditorGUILayout.Space(5);
            restoreConfig.backupGameObjectHierarchy = EditorGUILayout.Toggle("GameObject Hierarchy", restoreConfig.backupGameObjectHierarchy);

            EditorGUILayout.Space(10);

            // Progress
            if (isRestoring)
            {
                EditorGUILayout.LabelField("Progress", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(progressMessage);
                EditorGUI.ProgressBar(GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true)), progressValue, $"{progressValue * 100:F1}%");
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(10);
            }

            // Restore Button
            EditorGUI.BeginDisabledGroup(isBackingUp || isRestoring || string.IsNullOrEmpty(selectedBackupPath) || !IsValidBackupFolder(selectedBackupPath));
            if (GUILayout.Button("Restore from Backup", GUILayout.Height(30)))
            {
                RestoreBackup();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        private void CreateBackup()
        {
            if (backupScope != BackupScope.AllAssets && targetGameObject == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a target GameObject.", "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Create Backup",
                $"Create backup with scope: {backupScope}?\n\n" +
                $"Materials: {backupConfig.backupMaterials}\n" +
                $"Components: {backupConfig.backupComponents}\n" +
                $"Textures: {backupConfig.backupTextures}\n" +
                $"GameObject Hierarchy: {backupConfig.backupGameObjectHierarchy}",
                "Yes",
                "Cancel"
            );

            if (!confirmed)
                return;

            isBackingUp = true;
            progressMessage = "Starting backup...";
            progressValue = 0f;

            try
            {
                string backupPath = BackupSystem.CreateBackup(
                    backupConfig,
                    backupScope,
                    targetGameObject,
                    (message, progress) =>
                    {
                        progressMessage = message;
                        progressValue = progress;
                        Repaint();
                    }
                );

                if (!string.IsNullOrEmpty(backupPath))
                {
                    selectedBackupPath = backupPath;
                    EditorUtility.DisplayDialog(
                        "Backup Complete",
                        $"Backup created successfully!\n\n{backupPath}",
                        "OK"
                    );
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Failed to create backup. Check console for details.", "OK");
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Backup failed: {e.Message}", "OK");
                Debug.LogError($"Backup System error: {e}");
            }
            finally
            {
                isBackingUp = false;
                progressMessage = "";
                progressValue = 0f;
                Repaint();
            }
        }

        private void RestoreBackup()
        {
            if (string.IsNullOrEmpty(selectedBackupPath) || !IsValidBackupFolder(selectedBackupPath))
            {
                EditorUtility.DisplayDialog("Error", "Please select a valid backup folder.", "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Restore Backup",
                $"Restore from backup?\n\n{selectedBackupPath}\n\n" +
                $"Materials: {restoreConfig.backupMaterials}\n" +
                $"Components: {restoreConfig.backupComponents}\n" +
                $"Textures: {restoreConfig.backupTextures}\n" +
                $"GameObject Hierarchy: {restoreConfig.backupGameObjectHierarchy}\n\n" +
                "Warning: This will overwrite existing data!",
                "Yes, Restore",
                "Cancel"
            );

            if (!confirmed)
                return;

            isRestoring = true;
            progressMessage = "Starting restore...";
            progressValue = 0f;

            try
            {
                bool success = BackupSystem.RestoreFromBackup(
                    selectedBackupPath,
                    restoreConfig,
                    (message, progress) =>
                    {
                        progressMessage = message;
                        progressValue = progress;
                        Repaint();
                    }
                );

                if (success)
                {
                    EditorUtility.DisplayDialog("Success", "Backup restored successfully!", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Failed to restore backup. Check console for details.", "OK");
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Restore failed: {e.Message}", "OK");
                Debug.LogError($"Backup System error: {e}");
            }
            finally
            {
                isRestoring = false;
                progressMessage = "";
                progressValue = 0f;
                Repaint();
            }
        }

        /// <summary>
        /// Validates that the selected path is a valid backup folder
        /// </summary>
        private bool IsValidBackupFolder(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // Convert relative path to absolute if needed
            string fullPath = path;
            if (path.StartsWith("Assets"))
            {
                fullPath = Path.Combine(Application.dataPath, path.Substring(7)); // Remove "Assets/"
            }

            if (!Directory.Exists(fullPath))
                return false;

            // Check if folder contains at least one backup file
            string backupJsonPath = Path.Combine(fullPath, "backup.json");
            string materialsPath = Path.Combine(fullPath, "materials.json");
            string componentsPath = Path.Combine(fullPath, "components.json");
            string texturesPath = Path.Combine(fullPath, "textures.json");
            string hierarchyPath = Path.Combine(fullPath, "hierarchy.json");

            // Valid if it has the metadata file or at least one data file
            return File.Exists(backupJsonPath) || 
                   File.Exists(materialsPath) || 
                   File.Exists(componentsPath) || 
                   File.Exists(texturesPath) || 
                   File.Exists(hierarchyPath);
        }
    }
}
