using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VRCQuestPatcher
{
    /// <summary>
    /// Modern Editor Window for VRC-QuestPatcher
    /// </summary>
    public class VRCQuestPatcherWindow : EditorWindow
    {
        private GameObject avatarRoot;
        private VRCQuestPatcherCore.ConversionConfig config = new VRCQuestPatcherCore.ConversionConfig();
        private ConversionSummary summary;
        private QuestSDKEvaluator.AvatarStats currentStats;
        private bool isConverting = false;
        private string progressMessage = "";
        private float progressValue = 0f;
        private Vector2 scrollPosition;

        [MenuItem("Bluscream/Quest Patcher/Quest Patcher")]
        public static void ShowWindow()
        {
            VRCQuestPatcherWindow window = GetWindow<VRCQuestPatcherWindow>("QuestPatcher");
            window.minSize = new Vector2(520, 650);
            window.Show();
        }

        private void OnEnable()
        {
            LoadPreferences();
        }

        private void LoadPreferences()
        {
            config.TargetRank = (QuestPerformanceRank)EditorPrefs.GetInt("VRCQuestPatcher_TargetRank", (int)QuestPerformanceRank.Medium);
            config.PlacementLocation = (AssetPlacementLocation)EditorPrefs.GetInt("VRCQuestPatcher_PlacementLocation", (int)AssetPlacementLocation.SeparateFolder);
            config.PruningStrategy = (PhysBonePruningStrategy)EditorPrefs.GetInt("VRCQuestPatcher_PruningStrategy", (int)PhysBonePruningStrategy.DeepestFirst);
            config.DuplicateAvatar = EditorPrefs.GetBool("VRCQuestPatcher_DuplicateAvatar", true);
            config.AddPlatformSuffixes = EditorPrefs.GetBool("VRCQuestPatcher_AddPlatformSuffixes", true);
            config.RemapAnimationsAndVRCFury = EditorPrefs.GetBool("VRCQuestPatcher_RemapAnimationsAndVRCFury", true);
            config.ReplaceShaders = EditorPrefs.GetBool("VRCQuestPatcher_ReplaceShaders", true);
            config.OptimizeTextures = EditorPrefs.GetBool("VRCQuestPatcher_OptimizeTextures", true);
            config.MaxTextureSize = EditorPrefs.GetInt("VRCQuestPatcher_MaxTextureSize", 1024);
            config.PrunePhysBones = EditorPrefs.GetBool("VRCQuestPatcher_PrunePhysBones", true);
            config.DecimateMeshes = EditorPrefs.GetBool("VRCQuestPatcher_DecimateMeshes", true);
            config.RemoveIncompatibleComponents = EditorPrefs.GetBool("VRCQuestPatcher_RemoveIncompatibleComponents", true);
        }

        private void SavePreferences()
        {
            EditorPrefs.SetInt("VRCQuestPatcher_TargetRank", (int)config.TargetRank);
            EditorPrefs.SetInt("VRCQuestPatcher_PlacementLocation", (int)config.PlacementLocation);
            EditorPrefs.SetInt("VRCQuestPatcher_PruningStrategy", (int)config.PruningStrategy);
            EditorPrefs.SetBool("VRCQuestPatcher_DuplicateAvatar", config.DuplicateAvatar);
            EditorPrefs.SetBool("VRCQuestPatcher_AddPlatformSuffixes", config.AddPlatformSuffixes);
            EditorPrefs.SetBool("VRCQuestPatcher_RemapAnimationsAndVRCFury", config.RemapAnimationsAndVRCFury);
            EditorPrefs.SetBool("VRCQuestPatcher_ReplaceShaders", config.ReplaceShaders);
            EditorPrefs.SetBool("VRCQuestPatcher_OptimizeTextures", config.OptimizeTextures);
            EditorPrefs.SetInt("VRCQuestPatcher_MaxTextureSize", config.MaxTextureSize);
            EditorPrefs.SetBool("VRCQuestPatcher_PrunePhysBones", config.PrunePhysBones);
            EditorPrefs.SetBool("VRCQuestPatcher_DecimateMeshes", config.DecimateMeshes);
            EditorPrefs.SetBool("VRCQuestPatcher_RemoveIncompatibleComponents", config.RemoveIncompatibleComponents);
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("VRC-QuestPatcher", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Convert PC VRChat avatars into fully compliant Quest/Android avatars with one click. Automatically duplicates materials, remaps VRCFury toggles & material swaps, optimizes texture memory budgets, decimates meshes, and prunes PhysBones to hit target performance ranks.", MessageType.Info);
            EditorGUILayout.Space(10);

            // Avatar Root Selection
            EditorGUILayout.LabelField("1. Avatar Root Selection", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            Rect dropArea = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, avatarRoot != null ? $"Selected Avatar: {avatarRoot.name}" : "Drag Avatar Root GameObject Here\n(Must have VRC_AvatarDescriptor)", EditorStyles.helpBox);
            
            HandleDragAndDrop(dropArea);
            
            if (avatarRoot != null)
            {
                EditorGUILayout.BeginHorizontal();
                var newRoot = (GameObject)EditorGUILayout.ObjectField("Avatar Root", avatarRoot, typeof(GameObject), true);
                if (newRoot != avatarRoot)
                {
                    avatarRoot = newRoot;
                    UpdateStats();
                }
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                {
                    avatarRoot = null;
                    currentStats = null;
                }
                EditorGUILayout.EndHorizontal();

                if (currentStats != null)
                {
                    EditorGUILayout.HelpBox(
                        $"Current Avatar Rating Estimate: {currentStats.RatingName}\n" +
                        $"• Poly Count: {currentStats.TriangleCount:N0} tris\n" +
                        $"• Material Slots: {currentStats.MaterialSlotCount}\n" +
                        $"• PhysBones: {currentStats.PhysBoneComponentCount} components ({currentStats.PhysBoneTransformCount} transforms)",
                        MessageType.None
                    );
                }
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            // Target Performance Level & Options
            EditorGUILayout.LabelField("2. Conversion Preferences", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();

            QuestPerformanceProfile currentProfile = QuestPerformanceProfile.GetProfile(config.TargetRank);
            string triStr = currentProfile.MaxTriangles == int.MaxValue ? "Unlimited" : $"{currentProfile.MaxTriangles:N0}";
            string matStr = currentProfile.MaxMaterialSlots == int.MaxValue ? "Unlimited" : $"{currentProfile.MaxMaterialSlots}";
            EditorGUILayout.HelpBox($"Target Rank '{config.TargetRank}' Profile Limits: {triStr} Tris, {matStr} Material Slots, {currentProfile.MaxPhysBoneComponents} PhysBones.", MessageType.None);

            EditorGUILayout.Space(5);
            config.DuplicateAvatar = EditorGUILayout.ToggleLeft("Duplicate Avatar GameObject", config.DuplicateAvatar);
            if (config.DuplicateAvatar)
            {
                EditorGUI.indentLevel++;
                config.AddPlatformSuffixes = EditorGUILayout.ToggleLeft("Add Platform Suffixes ((PC) / (Quest))", config.AddPlatformSuffixes);
                EditorGUI.indentLevel--;
            }

            config.PlacementLocation = (AssetPlacementLocation)EditorGUILayout.EnumPopup("Asset Placement Location", config.PlacementLocation);
            EditorGUILayout.HelpBox(
                config.PlacementLocation == AssetPlacementLocation.SeparateFolder
                    ? "Saves generated Quest materials and animation clips into 'Assets/QuestPatched/<AvatarName>/'."
                    : "Saves generated Quest materials and animation clips in the same folder as the original assets with ' (Quest)' suffix.",
                MessageType.None
            );

            EditorGUILayout.Space(5);
            config.RemapAnimationsAndVRCFury = EditorGUILayout.ToggleLeft("Remap VRCFury & Animation Clips", config.RemapAnimationsAndVRCFury);
            config.ReplaceShaders = EditorGUILayout.ToggleLeft("Replace Shaders with Mobile Shaders", config.ReplaceShaders);
            config.OptimizeTextures = EditorGUILayout.ToggleLeft("Optimize Texture Memory Budget", config.OptimizeTextures);
            config.PruningStrategy = (PhysBonePruningStrategy)EditorGUILayout.EnumPopup("PhysBone Pruning Strategy", config.PruningStrategy);
            config.DecimateMeshes = EditorGUILayout.ToggleLeft("Decimate Meshes to Poly Limit", config.DecimateMeshes);
            config.RemoveIncompatibleComponents = EditorGUILayout.ToggleLeft("Remove Incompatible Components", config.RemoveIncompatibleComponents);

            if (EditorGUI.EndChangeCheck())
            {
                SavePreferences();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            // Progress
            if (isConverting)
            {
                EditorGUILayout.LabelField("Progress & Conversion Status", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(progressMessage, EditorStyles.boldLabel);
                if (!string.IsNullOrEmpty(timeDetailsMessage))
                {
                    EditorGUILayout.LabelField(timeDetailsMessage, EditorStyles.miniLabel);
                }
                EditorGUI.ProgressBar(GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true)), progressValue, $"{progressValue * 100:F1}%");
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(10);
            }

            // Action Button
            EditorGUI.BeginDisabledGroup(isConverting || avatarRoot == null);
            
            if (GUILayout.Button("Patch Avatar for Quest", GUILayout.Height(38)))
            {
                StartConversion();
            }
            
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.Space(10);

            // Summary Results
            if (summary != null)
            {
                summary.RenderGUI();
            }

            EditorGUILayout.EndScrollView();

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
                                UpdateStats();
                            }
                        }
                        currentEvent.Use();
                    }
                }
            }
        }

        private void UpdateStats()
        {
            if (avatarRoot != null)
            {
                currentStats = QuestSDKEvaluator.EvaluateAvatar(avatarRoot);
            }
        }

        private System.Diagnostics.Stopwatch conversionStopwatch = new System.Diagnostics.Stopwatch();
        private string timeDetailsMessage = "";

        private void StartConversion()
        {
            if (avatarRoot == null) return;

            isConverting = true;
            summary = new ConversionSummary();
            progressMessage = "Starting conversion...";
            progressValue = 0f;
            timeDetailsMessage = "Estimating time remaining...";
            conversionStopwatch.Restart();

            try
            {
                summary = VRCQuestPatcherCore.ConvertAvatar(
                    avatarRoot,
                    config,
                    (message, progress) =>
                    {
                        progressMessage = message;
                        progressValue = Math.Max(0f, Math.Min(1f, progress));

                        TimeSpan elapsed = conversionStopwatch.Elapsed;
                        string elapsedStr = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
                        
                        if (progress > 0.03f && progress < 0.99f)
                        {
                            double totalEstimatedSeconds = elapsed.TotalSeconds / progress;
                            double remainingSeconds = Math.Max(0, totalEstimatedSeconds - elapsed.TotalSeconds);
                            TimeSpan remaining = TimeSpan.FromSeconds(remainingSeconds);
                            string remainingStr = $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
                            timeDetailsMessage = $"Elapsed: {elapsedStr} | Estimated Remaining: ~{remainingStr}";
                        }
                        else if (progress >= 0.99f)
                        {
                            timeDetailsMessage = $"Completed in {elapsedStr}";
                        }
                        else
                        {
                            timeDetailsMessage = $"Elapsed: {elapsedStr} | Estimating remaining time...";
                        }

                        bool cancelRequested = EditorUtility.DisplayCancelableProgressBar(
                            "VRC-QuestPatcher",
                            $"{progressMessage}\n{timeDetailsMessage}",
                            progressValue
                        );

                        if (cancelRequested)
                        {
                            throw new OperationCanceledException("Quest conversion canceled by user.");
                        }

                        Repaint();
                    }
                );

                conversionStopwatch.Stop();
                TimeSpan totalTime = conversionStopwatch.Elapsed;

                EditorUtility.DisplayDialog(
                    "Quest Patch Complete",
                    $"Quest conversion completed successfully in {totalTime.Minutes:D2}:{totalTime.Seconds:D2}!\n\n" +
                    $"Materials Replaced: {summary.materialsReplaced}\n" +
                    $"Components Removed: {summary.componentsRemoved}\n" +
                    $"Textures Optimized: {summary.texturesOptimized}\n" +
                    $"\nErrors: {summary.errors.Count}\n" +
                    $"Warnings: {summary.warnings.Count}",
                    "OK"
                );
            }
            catch (OperationCanceledException canceledEx)
            {
                Debug.LogWarning($"[VRCQuestPatcherWindow] {canceledEx.Message}");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Conversion failed: {e.Message}", "OK");
                Debug.LogError($"[VRCQuestPatcherWindow] Conversion error: {e}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                conversionStopwatch.Stop();
                isConverting = false;
                progressMessage = "";
                progressValue = 0f;
                timeDetailsMessage = "";
                Repaint();
            }
        }
    }
}
