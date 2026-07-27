using Bluscream.VRC;
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
            int storedStrategy = EditorPrefs.GetInt("VRCAvatarOptimizer_PruningStrategy", (int)PhysBonePruningStrategy.DeepestFirst);
            config.PruningStrategy = Enum.IsDefined(typeof(PhysBonePruningStrategy), storedStrategy)
                ? (PhysBonePruningStrategy)storedStrategy
                : PhysBonePruningStrategy.DeepestFirst;
            config.DuplicateAvatar = EditorPrefs.GetBool("VRCAvatarOptimizer_DuplicateAvatar", true);
            config.AddPlatformSuffixes = EditorPrefs.GetBool("VRCAvatarOptimizer_AddPlatformSuffixes", true);
            config.RemapAnimationsAndVRCFury = EditorPrefs.GetBool("VRCAvatarOptimizer_RemapAnimationsAndVRCFury", true);
            config.ReplaceShaders = EditorPrefs.GetBool("VRCAvatarOptimizer_ReplaceShaders", true);
            config.OptimizeTextures = EditorPrefs.GetBool("VRCAvatarOptimizer_OptimizeTextures", true);
            config.MaxTextureResolution = EditorPrefs.GetInt("VRCAvatarOptimizer_MaxTextureResolution", 2048);
            config.CrunchCompressionQuality = EditorPrefs.GetInt("VRCAvatarOptimizer_CrunchCompressionQuality", 75);
            config.UncompressedAvatarHeadroomMB = EditorPrefs.GetFloat("VRCAvatarOptimizer_UncompressedAvatarHeadroomMB", 4.0f);
            config.CompressedAvatarHeadroomMB = EditorPrefs.GetFloat("VRCAvatarOptimizer_CompressedAvatarHeadroomMB", 1.5f);
            config.CrunchStepPercent = EditorPrefs.GetInt("VRCAvatarOptimizer_CrunchStepPercent", 10);
            config.DecimateMeshes = EditorPrefs.GetBool("VRCAvatarOptimizer_DecimateMeshes", true);
            config.RemoveIncompatibleComponents = EditorPrefs.GetBool("VRCAvatarOptimizer_RemoveIncompatibleComponents", true);
            config.DeletePlacementLocationBeforeConversion = EditorPrefs.GetBool("VRCAvatarOptimizer_DeletePlacementLocationBeforeConversion", false);
            config.DeleteExistingTargetGameObjects = EditorPrefs.GetBool("VRCAvatarOptimizer_DeleteExistingTargetGameObjects", false);
            config.ClearEditorLogBeforeConversion = EditorPrefs.GetBool("VRCAvatarOptimizer_ClearEditorLogBeforeConversion", false);
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
            EditorPrefs.SetFloat("VRCAvatarOptimizer_UncompressedAvatarHeadroomMB", config.UncompressedAvatarHeadroomMB);
            EditorPrefs.SetFloat("VRCAvatarOptimizer_CompressedAvatarHeadroomMB", config.CompressedAvatarHeadroomMB);
            EditorPrefs.SetInt("VRCAvatarOptimizer_CrunchStepPercent", config.CrunchStepPercent);
            EditorPrefs.SetBool("VRCAvatarOptimizer_DecimateMeshes", config.DecimateMeshes);
            EditorPrefs.SetBool("VRCAvatarOptimizer_RemoveIncompatibleComponents", config.RemoveIncompatibleComponents);
            EditorPrefs.SetBool("VRCAvatarOptimizer_DeletePlacementLocationBeforeConversion", config.DeletePlacementLocationBeforeConversion);
            EditorPrefs.SetBool("VRCAvatarOptimizer_DeleteExistingTargetGameObjects", config.DeleteExistingTargetGameObjects);
            EditorPrefs.SetBool("VRCAvatarOptimizer_ClearEditorLogBeforeConversion", config.ClearEditorLogBeforeConversion);
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

            // Enforce label width to leave at least 50% width for control inputs without overlapping labels
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Math.Max(220f, EditorGUIUtility.currentViewWidth * 0.55f);

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
                config.DeleteExistingTargetGameObjects = EditorGUILayout.ToggleLeft(
                    "Delete existing target GameObjects before starting", 
                    config.DeleteExistingTargetGameObjects
                );
                EditorGUI.indentLevel--;
            }

            config.PlacementLocation = (AssetPlacementLocation)EditorGUILayout.EnumPopup("Asset Placement Location", config.PlacementLocation);
            EditorGUILayout.HelpBox(
                config.PlacementLocation == AssetPlacementLocation.SeparateFolder
                    ? "Saves generated materials and animation clips into 'Assets/_AVATAROPTIMIZER/<AvatarName>/'."
                    : "Saves generated materials and animation clips in the same folder as the original assets.",
                MessageType.None
            );

            if (config.PlacementLocation == AssetPlacementLocation.SeparateFolder)
            {
                EditorGUI.indentLevel++;
                config.DeletePlacementLocationBeforeConversion = EditorGUILayout.ToggleLeft(
                    "Delete asset placement location before starting", 
                    config.DeletePlacementLocationBeforeConversion
                );
                EditorGUI.indentLevel--;
            }

            config.ClearEditorLogBeforeConversion = EditorGUILayout.ToggleLeft(
                "Clear Unity Editor.log before starting conversion", 
                config.ClearEditorLogBeforeConversion
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

                config.UncompressedAvatarHeadroomMB = EditorGUILayout.Slider("Uncompressed Headroom (MB)", config.UncompressedAvatarHeadroomMB, 0.0f, 15.0f);
                EditorGUILayout.HelpBox(
                    $"Uncompressed Headroom: {config.UncompressedAvatarHeadroomMB:F1} MB — Reserves space for mesh/animation payload out of the 40.00 MB limit (Texture VRAM target: {Math.Max(1.0f, 40.0f - config.UncompressedAvatarHeadroomMB):F1} MB).",
                    MessageType.None
                );

                config.CompressedAvatarHeadroomMB = EditorGUILayout.Slider("Compressed Headroom (MB)", config.CompressedAvatarHeadroomMB, 0.0f, 9.0f);
                EditorGUILayout.HelpBox(
                    $"Compressed Headroom: {config.CompressedAvatarHeadroomMB:F1} MB — Reserves space for non-texture bundle payload out of the 10.00 MB limit (Compressed texture bundle target: {Math.Max(0.5f, 10.0f - config.CompressedAvatarHeadroomMB):F1} MB).",
                    MessageType.None
                );

                config.CrunchStepPercent = EditorGUILayout.IntSlider("Crunch Step % (Quality Ladder)", config.CrunchStepPercent, 1, 50);
                EditorGUILayout.HelpBox(
                    $"Crunch Step: {config.CrunchStepPercent}% — Controls granularity of the quality ladder in Step 5 estimator and Step 8.5 build verification. Smaller = finer steps but more builds (slower). E.g. 10% = 10 Crunch levels, 25% = 4 levels.",
                    MessageType.None
                );
                EditorGUI.indentLevel--;
            }

            config.PruningStrategy = (PhysBonePruningStrategy)EditorGUILayout.EnumPopup("PhysBone Pruning Strategy", config.PruningStrategy);
            config.DecimateMeshes = EditorGUILayout.ToggleLeft("Decimate Meshes to Poly Limit", config.DecimateMeshes);
            config.RemoveIncompatibleComponents = EditorGUILayout.ToggleLeft("Remove Incompatible Components", config.RemoveIncompatibleComponents);

            EditorGUIUtility.labelWidth = prevLabelWidth;

            if (EditorGUI.EndChangeCheck())
            {
                SavePreferences();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            // The conversion runs synchronously with a modal progress bar, so no in-window
            // progress section is needed.

            // Avatar Descriptor validation
            bool hasDescriptor = avatarRoot != null && HasAvatarDescriptor(avatarRoot);
            if (avatarRoot != null && !hasDescriptor)
            {
                EditorGUILayout.HelpBox($"'{avatarRoot.name}' has no VRC Avatar Descriptor component. Select the avatar root GameObject.", MessageType.Error);
            }

            // Action Button
            EditorGUI.BeginDisabledGroup(isConverting || avatarRoot == null || !hasDescriptor);

            if (GUILayout.Button($"Optimize Avatar for {config.Platform}", GUILayout.Height(38)))
            {
                // Run outside OnGUI: modal dialogs/progress bars during the layout pass corrupt
                // IMGUI layout state ("EndLayoutGroup: BeginLayoutGroup must be called first").
                isConverting = true;
                EditorApplication.delayCall += StartConversion;
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

        /// <summary>
        /// Checks for a VRC Avatar Descriptor by type name so this window has no hard SDK dependency.
        /// </summary>
        private static bool HasAvatarDescriptor(GameObject go)
        {
            return go != null && go.GetComponents<Component>().Any(c => c != null && c.GetType().Name.Contains("AvatarDescriptor"));
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
            if (avatarRoot == null)
            {
                isConverting = false;
                return;
            }

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
