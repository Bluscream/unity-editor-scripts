using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Bluscream;
using static Bluscream.Utils;

namespace Bluscream.Cleanup
{
    /// <summary>
    /// Editor window for asset cleanup
    /// </summary>
    public class CleanupWindow : EditorWindow
    {
        private Vector2 scrollPosition;
        public List<AssetDeletionInfo> unusedAssets = new List<AssetDeletionInfo>();
        private bool hasAnalyzed = false;
        public string targetFolder = "Assets";
        public bool recursive = true;
        private string analysisStatus = "";

        [MenuItem("Tools/Asset Cleanup")]
        public static CleanupWindow ShowWindow()
        {
            CleanupWindow window = GetWindow<CleanupWindow>("Asset Cleanup");
            window.minSize = new Vector2(600, 400);
            return window;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Asset Cleanup", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Configuration
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target Folder:", GUILayout.Width(100));
            targetFolder = EditorGUILayout.TextField(targetFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string selected = EditorUtility.OpenFolderPanel("Select Folder", targetFolder, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    // Convert to Assets-relative path
                    string projectPath = Application.dataPath.Replace("/Assets", "").Replace("\\Assets", "");
                    if (selected.StartsWith(projectPath))
                    {
                        targetFolder = "Assets" + selected.Substring(projectPath.Length).Replace("\\", "/");
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            recursive = EditorGUILayout.Toggle("Recursive", recursive);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Analysis
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Analysis", EditorStyles.boldLabel);
            
            if (!string.IsNullOrEmpty(analysisStatus))
            {
                EditorGUILayout.HelpBox(analysisStatus, MessageType.Info);
            }

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(targetFolder) || !Directory.Exists(targetFolder));
            if (GUILayout.Button("Analyze Unused Assets", GUILayout.Height(30)))
            {
                AnalyzeAssets();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Results
            if (hasAnalyzed)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Found {unusedAssets.Count} unused assets", EditorStyles.boldLabel);
                
                if (unusedAssets.Count > 0)
                {
                    long totalSize = unusedAssets.Sum(a => a.sizeInBytes);
                    EditorGUILayout.LabelField($"Total size: {Utils.FormatBytes(totalSize)}");
                    EditorGUILayout.Space(5);

                    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
                    foreach (AssetDeletionInfo info in unusedAssets)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(info.assetPath, GUILayout.Width(300));
                        EditorGUILayout.LabelField(info.assetType, GUILayout.Width(100));
                        EditorGUILayout.LabelField(Utils.FormatBytes(info.sizeInBytes), GUILayout.Width(80));
                        EditorGUILayout.LabelField(info.reason, GUILayout.Width(200));
                        if (GUILayout.Button("Ping", GUILayout.Width(50)))
                        {
                            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(info.assetPath);
                            if (obj != null)
                            {
                                EditorGUIUtility.PingObject(obj);
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();

                    EditorGUILayout.Space(5);
                    EditorGUI.BeginDisabledGroup(unusedAssets.Count == 0);
                    if (GUILayout.Button($"Delete {unusedAssets.Count} Unused Assets", GUILayout.Height(30)))
                    {
                        ShowDeletionConfirmation();
                    }
                    EditorGUI.EndDisabledGroup();
                }
                else
                {
                    EditorGUILayout.HelpBox("No unused assets found!", MessageType.Info);
                }
                EditorGUILayout.EndVertical();
            }
        }

        private void AnalyzeAssets()
        {
            hasAnalyzed = false;
            unusedAssets.Clear();
            analysisStatus = "Analyzing...";

            try
            {
                EditorUtility.DisplayProgressBar("Asset Cleanup", "Analyzing unused assets...", 0f);
                
                unusedAssets = AssetCleanup.AnalyzeUnusedAssets(targetFolder, recursive, (message, progress) =>
                {
                    EditorUtility.DisplayProgressBar("Asset Cleanup", message, progress);
                });

                hasAnalyzed = true;
                analysisStatus = $"Analysis complete. Found {unusedAssets.Count} unused assets.";
            }
            catch (Exception e)
            {
                analysisStatus = $"Analysis failed: {e.Message}";
                Debug.LogError($"Asset cleanup analysis failed: {e}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void ShowDeletionConfirmation()
        {
            // Create backup first
            if (Utils.IsBackupSystemAvailable())
            {
                if (!CreateBackupBeforeCleanup())
                {
                    return; // User cancelled backup
                }
            }
            else
            {
                if (!EditorUtility.DisplayDialog(
                    "No Backup System",
                    "The backup system is not available. This operation cannot be undone. Continue?",
                    "Continue",
                    "Cancel"))
                {
                    return;
                }
            }

            // Show summary
            long totalSize = unusedAssets.Sum(a => a.sizeInBytes);
            string message = $"You are about to delete {unusedAssets.Count} unused assets ({Utils.FormatBytes(totalSize)}).\n\n" +
                           "This action cannot be undone.\n\n" +
                           "Continue?";

            if (EditorUtility.DisplayDialog("Confirm Deletion", message, "Delete", "Cancel"))
            {
                DeleteAssets();
            }
        }

        private bool CreateBackupBeforeCleanup()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Creating Backup", "Preparing backup...", 0f);

                // Create backup folder
                string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string backupFolder = Path.Combine("Assets/Backups", $"CleanupBackup_{timestamp}");
                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                }

                // Use reflection to call AssetBackupHandler
                System.Type assetBackupType = null;
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    assetBackupType = assembly.GetType("Bluscream.BackupSystem.AssetBackupHandler");
                    if (assetBackupType != null) break;
                }

                if (assetBackupType != null)
                {
                    // Get BackupScope enum
                    System.Type backupScopeType = null;
                    foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        backupScopeType = assembly.GetType("Bluscream.BackupSystem.BackupScope");
                        if (backupScopeType != null) break;
                    }

                    if (backupScopeType != null)
                    {
                        object scope = Enum.Parse(backupScopeType, "AllAssets");
                        string assetsCsvPath = Path.Combine(backupFolder, "assets.csv");

                        // Call BackupAssetsToCsv
                        System.Reflection.MethodInfo backupMethod = assetBackupType.GetMethod("BackupAssetsToCsv",
                            new System.Type[] { backupScopeType, typeof(GameObject), typeof(string), typeof(Action<string, float>) });

                        if (backupMethod != null)
                        {
                            backupMethod.Invoke(null, new object[] { scope, null, assetsCsvPath,
                                new Action<string, float>((msg, prog) =>
                                {
                                    EditorUtility.DisplayProgressBar("Creating Backup", msg, prog);
                                })
                            });

                            EditorUtility.ClearProgressBar();

                            if (File.Exists(assetsCsvPath))
                            {
                                EditorUtility.DisplayDialog("Backup Created", $"Backup created at:\n{backupFolder}\n\nThis backup includes assets.csv with all asset information.", "OK");
                                return true;
                            }
                        }
                    }
                }

                EditorUtility.ClearProgressBar();
                Debug.LogWarning("Could not create assets.csv backup. Backup system may not be available.");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"Failed to create backup: {e}");
                if (!EditorUtility.DisplayDialog(
                    "Backup Failed",
                    $"Failed to create backup: {e.Message}\n\nContinue without backup?",
                    "Continue",
                    "Cancel"))
                {
                    return false;
                }
            }

            return true;
        }

        private void DeleteAssets()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Deleting Assets", "Deleting unused assets...", 0f);
                
                AssetCleanup.DeleteAssets(unusedAssets, (message, progress) =>
                {
                    EditorUtility.DisplayProgressBar("Deleting Assets", message, progress);
                });

                EditorUtility.DisplayDialog("Cleanup Complete", $"Successfully deleted {unusedAssets.Count} unused assets.", "OK");
                
                unusedAssets.Clear();
                hasAnalyzed = false;
                analysisStatus = "Cleanup complete. Re-analyze to check for more unused assets.";
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Cleanup Failed", $"Failed to delete assets: {e.Message}", "OK");
                Debug.LogError($"Asset cleanup failed: {e}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

    }
}
