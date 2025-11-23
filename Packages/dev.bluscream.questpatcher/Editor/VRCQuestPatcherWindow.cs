using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using static Bluscream.Utils;

namespace VRCQuestPatcher
{
    /// <summary>
    /// Main editor window for VRC-QuestPatcher
    /// </summary>
    public class VRCQuestPatcherWindow : EditorWindow
    {
        private GameObject avatarRoot;
        private VRCQuestPatcherCore.ConversionConfig config = new VRCQuestPatcherCore.ConversionConfig();
        private ConversionSummary summary;
        private bool isConverting = false;
        private string progressMessage = "";
        private float progressValue = 0f;
        private Vector2 scrollPosition;
        private string lastBackupPath = "";

        [MenuItem("VRChat/Quest Patcher")]
        public static void ShowWindow()
        {
            VRCQuestPatcherWindow window = GetWindow<VRCQuestPatcherWindow>("QuestPatcher");
            window.minSize = new Vector2(500, 600);
            window.Show();
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("QuestPatcher", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Convert VRChat avatars for Quest/Android compatibility by removing incompatible components, replacing shaders, and applying optimizations.", MessageType.Info);
            EditorGUILayout.Space(10);

            // Avatar Root Selection
            EditorGUILayout.LabelField("Avatar Root", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Drag and drop area
            Rect dropArea = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, avatarRoot != null ? $"Avatar: {avatarRoot.name}" : "Drag Avatar Root GameObject here\n(Must have VRC_AvatarDescriptor)", EditorStyles.helpBox);
            
            HandleDragAndDrop(dropArea);
            
            if (avatarRoot != null)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField("Avatar Root", avatarRoot, typeof(GameObject), true);
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                {
                    avatarRoot = null;
                }
                EditorGUILayout.EndHorizontal();

                // Validate avatar descriptor
                if (!HasAvatarDescriptor(avatarRoot))
                {
                    EditorGUILayout.HelpBox("Warning: Selected GameObject does not have VRC_AvatarDescriptor component.", MessageType.Warning);
                }
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            // Configuration
            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            config.removeComponents = EditorGUILayout.Toggle("Remove Incompatible Components", config.removeComponents);
            EditorGUILayout.HelpBox("Removes DynamicBones, Cloth, Cameras, Lights, AudioSources, Physics components, etc.", MessageType.None);
            
            EditorGUILayout.Space(5);
            config.replaceShaders = EditorGUILayout.Toggle("Replace Shaders", config.replaceShaders);
            EditorGUILayout.HelpBox("Replaces PC shaders with Quest-compatible VRChat/Mobile shaders.", MessageType.None);
            
            EditorGUILayout.Space(5);
            config.optimizeTextures = EditorGUILayout.Toggle("Optimize Textures", config.optimizeTextures);
            if (config.optimizeTextures)
            {
                EditorGUI.indentLevel++;
                config.maxTextureSize = EditorGUILayout.IntSlider("Max Texture Size", config.maxTextureSize, 256, 2048);
                config.compressionQuality = EditorGUILayout.IntSlider("Compression Quality", config.compressionQuality, 0, 100);
                config.useCrunchCompression = EditorGUILayout.Toggle("Use Crunch Compression", config.useCrunchCompression);
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.Space(5);
            config.backupLocation = EditorGUILayout.TextField("Backup Location", config.backupLocation);
            EditorGUILayout.HelpBox("Location where backups will be stored. Default: Assets/VRCQuestPatcherBackups", MessageType.None);
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            // Progress
            if (isConverting)
            {
                EditorGUILayout.LabelField("Progress", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(progressMessage);
                EditorGUI.ProgressBar(GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true)), progressValue, $"{progressValue * 100:F1}%");
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(10);
            }

            // Action Buttons
            EditorGUI.BeginDisabledGroup(isConverting || avatarRoot == null);
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Start Conversion", GUILayout.Height(30)))
            {
                StartConversion();
            }
            
            EditorGUI.EndDisabledGroup();
            
            // Check if backup path exists (can be file or folder)
            bool backupPathExists = !string.IsNullOrEmpty(lastBackupPath) && 
                (System.IO.File.Exists(lastBackupPath) || System.IO.Directory.Exists(lastBackupPath));
            
            EditorGUI.BeginDisabledGroup(isConverting || !backupPathExists || !IsBackupSystemAvailable());
            if (GUILayout.Button("Restore from Backup", GUILayout.Height(30)))
            {
                RestoreBackup();
            }
            EditorGUI.EndDisabledGroup();
            
            if (!IsBackupSystemAvailable())
            {
                EditorGUILayout.HelpBox("BackupSystem package not found. Restore functionality is disabled. Install dev.bluscream.backupsystem to enable restore.", MessageType.Warning);
            }
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);

            // Summary
            if (summary != null)
            {
                summary.RenderGUI();
            }

            EditorGUILayout.EndScrollView();

            // Handle progress updates
            if (isConverting)
            {
                Repaint();
            }
        }

        private void HandleDragAndDrop(Rect dropArea)
        {
            Event currentEvent = Event.current;

            if (currentEvent.type == EventType.DragUpdated || currentEvent.type == EventType.DragPerform)
            {
                if (dropArea.Contains(currentEvent.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDrop.objectReferences.Length > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

                    if (currentEvent.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();

                        if (DragAndDrop.objectReferences.Length > 0)
                        {
                            GameObject draggedObject = DragAndDrop.objectReferences[0] as GameObject;
                            if (draggedObject != null)
                            {
                                avatarRoot = draggedObject;
                            }
                        }

                        currentEvent.Use();
                    }
                }
            }
        }

        private bool HasAvatarDescriptor(GameObject obj)
        {
            if (obj == null) return false;

            Component[] components = obj.GetComponents<Component>();
            foreach (Component comp in components)
            {
                if (comp == null) continue;
                string typeName = comp.GetType().FullName;
                if (typeName.Contains("VRC_AvatarDescriptor") || typeName.Contains("VRCAvatarDescriptor"))
                {
                    return true;
                }
            }

            return false;
        }

        private void StartConversion()
        {
            if (avatarRoot == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select an avatar root GameObject.", "OK");
                return;
            }

            if (!HasAvatarDescriptor(avatarRoot))
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Warning",
                    "Selected GameObject does not have VRC_AvatarDescriptor component. Continue anyway?",
                    "Yes",
                    "No"
                );

                if (!proceed)
                    return;
            }

            // Check for BackupSystem and confirm
            if (!IsBackupSystemAvailable())
            {
                bool proceedWithoutBackup = BackupSystemHelper.ConfirmWithoutBackupSystem();
                if (!proceedWithoutBackup)
                    return;
            }
            else
            {
                // Confirm conversion (with backup available)
                bool confirmed = EditorUtility.DisplayDialog(
                    "Confirm Conversion",
                    "This will modify your avatar. A backup will be created. Continue?",
                    "Yes",
                    "No"
                );

                if (!confirmed)
                    return;
            }

            isConverting = true;
            summary = new ConversionSummary();
            progressMessage = "Starting conversion...";
            progressValue = 0f;

            // Run conversion
            try
            {
                summary = VRCQuestPatcherCore.ConvertAvatar(
                    avatarRoot,
                    config,
                    (message, progress) =>
                    {
                        progressMessage = message;
                        progressValue = progress;
                        Repaint();
                    }
                );

                // Store backup path if available
                if (!string.IsNullOrEmpty(config.backupLocation))
                {
                    string[] backupFiles = Directory.GetFiles(config.backupLocation, "backup.json", SearchOption.AllDirectories);
                    if (backupFiles.Length > 0)
                    {
                        lastBackupPath = backupFiles[backupFiles.Length - 1]; // Get most recent
                    }
                }

                EditorUtility.DisplayDialog(
                    "Conversion Complete",
                    $"Conversion completed!\n\n" +
                    $"Materials Replaced: {summary.materialsReplaced}\n" +
                    $"Components Removed: {summary.componentsRemoved}\n" +
                    $"Textures Optimized: {summary.texturesOptimized}\n" +
                    $"\nErrors: {summary.errors.Count}\n" +
                    $"Warnings: {summary.warnings.Count}",
                    "OK"
                );
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Conversion failed: {e.Message}", "OK");
                Debug.LogError($"QuestPatcher error: {e}");
            }
            finally
            {
                isConverting = false;
                progressMessage = "";
                progressValue = 0f;
                Repaint();
            }
        }

        private void RestoreBackup()
        {
            if (string.IsNullOrEmpty(lastBackupPath))
            {
                EditorUtility.DisplayDialog("Error", "No backup path available.", "OK");
                return;
            }

            if (!IsBackupSystemAvailable())
            {
                EditorUtility.DisplayDialog("Error", "BackupSystem package is required for restore functionality. Please install dev.bluscream.backupsystem.", "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Restore Backup",
                $"Restore from backup?\n\n{lastBackupPath}",
                "Yes",
                "No"
            );

            if (!confirmed)
                return;

            // Try to use BackupSystem for restore
            try
            {
                System.Type backupSystemType = System.Type.GetType("Bluscream.BackupSystem.BackupSystem, Assembly-CSharp-Editor")
                    ?? System.Type.GetType("Bluscream.BackupSystem.BackupSystem");
                
                if (backupSystemType != null)
                {
                    System.Type configType = System.Type.GetType("Bluscream.BackupSystem.BackupConfig, Assembly-CSharp-Editor")
                        ?? System.Type.GetType("Bluscream.BackupSystem.BackupConfig");
                    
                    if (configType != null)
                    {
                        object restoreConfig = Activator.CreateInstance(configType);
                        configType.GetProperty("backupMaterials").SetValue(restoreConfig, true);
                        configType.GetProperty("backupComponents").SetValue(restoreConfig, true);
                        configType.GetProperty("backupTextures").SetValue(restoreConfig, true);
                        configType.GetProperty("backupGameObjectHierarchy").SetValue(restoreConfig, false);
                        configType.GetProperty("includeMaterialProperties").SetValue(restoreConfig, true);
                        configType.GetProperty("includeComponentData").SetValue(restoreConfig, true);

                        var restoreMethod = backupSystemType.GetMethod("RestoreFromBackup",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        
                        if (restoreMethod != null)
                        {
                            // Handle both file and folder paths
                            string backupFolder = lastBackupPath;
                            if (System.IO.File.Exists(lastBackupPath))
                            {
                                // If it's a file, get the directory
                                backupFolder = System.IO.Path.GetDirectoryName(lastBackupPath);
                            }
                            
                            object result = restoreMethod.Invoke(null, new object[] { backupFolder, restoreConfig, null });
                            bool success = result is bool b && b;
                            
                            if (success)
                            {
                                EditorUtility.DisplayDialog("Success", "Backup restored successfully!", "OK");
                                summary = null;
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to use BackupSystem for restore: {e.Message}");
            }

            // Fallback to BackupManager (legacy format - single JSON file)
            // Only try if it's a file path, not a folder
            if (System.IO.File.Exists(lastBackupPath))
            {
                bool success2 = BackupManager.RestoreFromBackup(lastBackupPath);
                if (success2)
                {
                    EditorUtility.DisplayDialog("Success", "Backup restored successfully!", "OK");
                    summary = null;
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Failed to restore backup. Check console for details.", "OK");
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Error", 
                    "BackupSystem package is required for folder-based backups. Please install dev.bluscream.backupsystem.", 
                    "OK");
            }
        }
    }
}
