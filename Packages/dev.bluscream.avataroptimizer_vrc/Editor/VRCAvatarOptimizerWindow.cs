using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Modern Editor Window for Avatar Optimizer (VRChat)
    /// </summary>
    public class VRCAvatarOptimizerWindow : EditorWindow
    {
        private GameObject avatarRoot;
        private VRCAvatarOptimizerCore.ConversionConfig config = new VRCAvatarOptimizerCore.ConversionConfig();
        private ConversionSummary summary;
        private AvatarSDKEvaluator.AvatarStats currentStats;
        private bool isConverting = false;
        private string progressMessage = "";
        private float progressValue = 0f;
        private Vector2 scrollPosition;

        [MenuItem("Bluscream/VRChat/Avatar Optimizer")]
        public static void ShowWindow()
        {
            VRCAvatarOptimizerWindow window = GetWindow<VRCAvatarOptimizerWindow>("Avatar Optimizer");
            window.minSize = new Vector2(540, 680);
            window.Show();
        }

        private void OnEnable()
        {
            LoadPreferences();
        }

        private void LoadPreferences()
        {
            config.Platform = (TargetPlatform)EditorPrefs.GetInt("VRCAvatarOptimizer_Platform", (int)TargetPlatform.Android);
            config.TargetRank = (AvatarPerformanceRank)EditorPrefs.GetInt("VRCAvatarOptimizer_TargetRank", (int)AvatarPerformanceRank.Medium);
            config.PlacementLocation = (AssetPlacementLocation)EditorPrefs.GetInt("VRCAvatarOptimizer_PlacementLocation", (int)AssetPlacementLocation.SeparateFolder);
            config.PruningStrategy = (PhysBonePruningStrategy)EditorPrefs.GetInt("VRCAvatarOptimizer_PruningStrategy", (int)PhysBonePruningStrategy.DeepestFirst);
            config.DuplicateAvatar = EditorPrefs.GetBool("VRCAvatarOptimizer_DuplicateAvatar", true);
            config.AddPlatformSuffixes = EditorPrefs.GetBool("VRCAvatarOptimizer_AddPlatformSuffixes", true);
            config.RemapAnimationsAndVRCFury = EditorPrefs.GetBool("VRCAvatarOptimizer_RemapAnimationsAndVRCFury", true);
            config.ReplaceShaders = EditorPrefs.GetBool("VRCAvatarOptimizer_ReplaceShaders", true);
            config.OptimizeTextures = EditorPrefs.GetBool("VRCAvatarOptimizer_OptimizeTextures", true);
            config.MaxTextureResolution = EditorPrefs.GetInt("VRCAvatarOptimizer_MaxTextureResolution", 2048);
            config.CrunchCompressionQuality = EditorPrefs.GetInt("VRCAvatarOptimizer_CrunchCompressionQuality", 75);
            config.PrunePhysBones = EditorPrefs.GetBool("VRCAvatarOptimizer_PrunePhysBones", true);
            config.DecimateMeshes = EditorPrefs.GetBool("VRCAvatarOptimizer_DecimateMeshes", true);
            config.RemoveIncompatibleComponents = EditorPrefs.GetBool("VRCAvatarOptimizer_RemoveIncompatibleComponents", true);
        }

        private void SavePreferences()
        {
            EditorPrefs.SetInt("VRCAvatarOptimizer_Platform", (int)config.Platform);
            EditorPrefs.SetInt("VRCAvatarOptimizer_TargetRank", (int)config.TargetRank);
            EditorPrefs.SetInt("VRCAvatarOptimizer_PlacementLocation", (int)config.PlacementLocation);
            EditorPrefs.SetInt("VRCAvatarOptimizer_PruningStrategy", (int)config.PruningStrategy);
            EditorPrefs.SetBool("VRCAvatarOptimizer_DuplicateAvatar", config.DuplicateAvatar);
            EditorPrefs.SetBool("VRCAvatarOptimizer_AddPlatformSuffixes", config.AddPlatformSuffixes);
            EditorPrefs.SetBool("VRCAvatarOptimizer_RemapAnimationsAndVRCFury", config.RemapAnimationsAndVRCFury);
            EditorPrefs.SetBool("VRCAvatarOptimizer_ReplaceShaders", config.ReplaceShaders);
            EditorPrefs.SetBool("VRCAvatarOptimizer_OptimizeTextures", config.OptimizeTextures);
            EditorPrefs.SetInt("VRCAvatarOptimizer_MaxTextureResolution", config.MaxTextureResolution);
            EditorPrefs.SetInt("VRCAvatarOptimizer_CrunchCompressionQuality", config.CrunchCompressionQuality);
            EditorPrefs.SetBool("VRCAvatarOptimizer_PrunePhysBones", config.PrunePhysBones);
            EditorPrefs.SetBool("VRCAvatarOptimizer_DecimateMeshes", config.DecimateMeshes);
            EditorPrefs.SetBool("VRCAvatarOptimizer_RemoveIncompatibleComponents", config.RemoveIncompatibleComponents);
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Avatar Optimizer (VRChat)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Optimize VRChat avatars for target platforms (PC & Android) according to SDK performance rank limits. Automatically duplicates materials, remaps VRCFury toggles & material swaps, optimizes texture memory budgets, decimates meshes, and prunes PhysBones to hit target performance ranks.", MessageType.Info);
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
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            // Target Performance Level & Options
            EditorGUILayout.LabelField("2. Conversion Preferences", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();

            config.Platform = (TargetPlatform)EditorGUILayout.EnumPopup("Target Platform", config.Platform);
            config.TargetRank = (AvatarPerformanceRank)EditorGUILayout.EnumPopup("Target Performance Rank", config.TargetRank);

            PlatformProfile currentProfile = PlatformProfile.GetProfile(config.Platform, config.TargetRank);
            string triStr = currentProfile.MaxTriangles == int.MaxValue ? "Unlimited" : $"{currentProfile.MaxTriangles:N0}";
            string matStr = currentProfile.MaxMaterialSlots == int.MaxValue ? "Unlimited" : $"{currentProfile.MaxMaterialSlots}";
            EditorGUILayout.HelpBox($"Profile Limits ({currentProfile.Platform} - {currentProfile.Rank}): {triStr} Tris, {matStr} Mat Slots, {currentProfile.MaxPhysBoneComponents} PhysBones, Bounds: {currentProfile.MaxBoundsSize.x}x{currentProfile.MaxBoundsSize.y}x{currentProfile.MaxBoundsSize.z}m.", MessageType.None);

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
                    ? "Saves generated materials and animation clips into 'Assets/_AVATAROPTIMIZER/<AvatarName>/'."
                    : "Saves generated materials and animation clips in the same folder as the original assets.",
                MessageType.None
            );

            EditorGUILayout.Space(5);
            config.RemapAnimationsAndVRCFury = EditorGUILayout.ToggleLeft("Remap VRCFury & Animation Clips", config.RemapAnimationsAndVRCFury);
            config.ReplaceShaders = EditorGUILayout.ToggleLeft("Replace Shaders with Mobile Shaders", config.ReplaceShaders);
            config.OptimizeTextures = EditorGUILayout.ToggleLeft("Optimize Texture Memory Budget", config.OptimizeTextures);
            if (config.OptimizeTextures)
            {
                EditorGUI.indentLevel++;
                int[] resValues = new int[] { 4096, 2048, 1024, 512, 256, 128 };
                string[] resLabels = new string[] { "4096 px", "2048 px (Recommended)", "1024 px", "512 px", "256 px", "128 px" };
                config.MaxTextureResolution = EditorGUILayout.IntPopup("Max Texture Resolution", config.MaxTextureResolution, resLabels, resValues);

                config.CrunchCompressionQuality = EditorGUILayout.IntSlider("Crunch Compression Ratio", config.CrunchCompressionQuality, 0, 100);
                EditorGUILayout.HelpBox(
                    config.CrunchCompressionQuality == 0 
                        ? "Crunching Disabled (Raw ASTC): Higher disk bundle size, maximum visual quality."
                        : $"Crunch Ratio: {config.CrunchCompressionQuality}% — Higher ratio = smaller AssetBundle size on disk.",
                    MessageType.None
                );
                EditorGUI.indentLevel--;
            }

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
            
            if (GUILayout.Button($"Optimize Avatar for {config.Platform}", GUILayout.Height(38)))
            {
                StartConversion();
            }
            
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.Space(10);

            // Current Avatar Rating Estimate
            if (avatarRoot != null && currentStats != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.HelpBox(
                    $"Current Avatar Rating Estimate: {currentStats.RatingName}\n" +
                    $"• Poly Count: {currentStats.TriangleCount:N0} tris\n" +
                    $"• Material Slots: {currentStats.MaterialSlotCount}\n" +
                    $"• PhysBones: {currentStats.PhysBoneComponentCount} components ({currentStats.PhysBoneTransformCount} transforms)",
                    MessageType.None
                );
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(10);
            }

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
                currentStats = AvatarSDKEvaluator.EvaluateAvatar(avatarRoot);
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
                summary = VRCAvatarOptimizerCore.ConvertAvatar(
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
                            "Avatar Optimizer",
                            $"{progressMessage}\n{timeDetailsMessage}",
                            progressValue
                        );

                        if (cancelRequested)
                        {
                            throw new OperationCanceledException("Avatar conversion canceled by user.");
                        }

                        Repaint();
                    }
                );

                conversionStopwatch.Stop();
                TimeSpan totalTime = conversionStopwatch.Elapsed;

                EditorUtility.DisplayDialog(
                    "Avatar Optimization Complete",
                    $"Avatar optimization completed successfully in {totalTime.Minutes:D2}:{totalTime.Seconds:D2}!\n\n" +
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
                Debug.LogWarning($"[VRCAvatarOptimizerWindow] {canceledEx.Message}");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Conversion failed: {e.Message}", "OK");
                Debug.LogError($"[VRCAvatarOptimizerWindow] Conversion error: {e}");
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
