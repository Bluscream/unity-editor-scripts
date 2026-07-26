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

            config.TargetRank = (QuestPerformanceRank)EditorGUILayout.EnumPopup("Target Performance Rank", config.TargetRank);
            EditorGUILayout.HelpBox($"Target Rank '{config.TargetRank}' Profile Limits: Max {QuestPerformanceProfile.GetProfile(config.TargetRank).MaxTriangles:N0} Tris, {QuestPerformanceProfile.GetProfile(config.TargetRank).MaxMaterialSlots} Material Slots, {QuestPerformanceProfile.GetProfile(config.TargetRank).MaxPhysBoneComponents} PhysBones.", MessageType.None);

            EditorGUILayout.Space(5);
            config.DuplicateAvatar = EditorGUILayout.Toggle("Duplicate Avatar GameObject", config.DuplicateAvatar);
            if (config.DuplicateAvatar)
            {
                EditorGUI.indentLevel++;
                config.AvatarSuffix = EditorGUILayout.TextField("Avatar Suffix", config.AvatarSuffix);
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
            config.RemapAnimationsAndVRCFury = EditorGUILayout.Toggle("Remap VRCFury & Animation Clips", config.RemapAnimationsAndVRCFury);
            config.ReplaceShaders = EditorGUILayout.Toggle("Replace Shaders with Mobile Shaders", config.ReplaceShaders);
            config.OptimizeTextures = EditorGUILayout.Toggle("Optimize Texture Memory Budget", config.OptimizeTextures);
            if (config.OptimizeTextures)
            {
                EditorGUI.indentLevel++;
                config.MaxTextureSize = EditorGUILayout.IntSlider("Max Texture Size", config.MaxTextureSize, 256, 2048);
                EditorGUI.indentLevel--;
            }
            config.PrunePhysBones = EditorGUILayout.Toggle("Prune Excess PhysBones to Target Rank", config.PrunePhysBones);
            if (config.PrunePhysBones)
            {
                EditorGUI.indentLevel++;
                config.PruningStrategy = (PhysBonePruningStrategy)EditorGUILayout.EnumPopup("Pruning Strategy", config.PruningStrategy);
                EditorGUI.indentLevel--;
            }
            config.DecimateMeshes = EditorGUILayout.Toggle("Decimate Meshes to Poly Limit", config.DecimateMeshes);
            config.RemoveIncompatibleComponents = EditorGUILayout.Toggle("Remove Incompatible Components", config.RemoveIncompatibleComponents);

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

        private void StartConversion()
        {
            if (avatarRoot == null) return;

            isConverting = true;
            summary = new ConversionSummary();
            progressMessage = "Starting conversion...";
            progressValue = 0f;

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

                EditorUtility.DisplayDialog(
                    "Quest Patch Complete",
                    $"Quest conversion completed successfully!\n\n" +
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
                Debug.LogError($"[VRCQuestPatcherWindow] Conversion error: {e}");
            }
            finally
            {
                isConverting = false;
                progressMessage = "";
                progressValue = 0f;
                Repaint();
            }
        }
    }
}
