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
        private bool cachedHasDescriptor = false;
        private PlatformProfile cachedProfile;

        [MenuItem("Bluscream/VRChat/Avatar Optimizer")]
        public static void ShowWindow()
        {
            VRCAvatarOptimizerWindow window = GetWindow<VRCAvatarOptimizerWindow>("Avatar Optimizer");
            window.minSize = new Vector2(540, 680);
            window.Show();
        }

        private System.Diagnostics.Stopwatch guiStopwatch = new System.Diagnostics.Stopwatch();

        private void OnEnable()
        {
            Debug.Log("[VRCAvatarOptimizerWindow] OnEnable called.");
            LoadPreferences();
        }

        private void OnDisable()
        {
            Debug.Log("[VRCAvatarOptimizerWindow] OnDisable called.");
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
            config.OptimizeFXLayer = EditorPrefs.GetBool("VRCAvatarOptimizer_OptimizeFXLayer", true);
            config.UseNaNimationToggles = EditorPrefs.GetBool("VRCAvatarOptimizer_UseNaNimationToggles", true);
            config.BakeNonAnimatedBlendshapes = EditorPrefs.GetBool("VRCAvatarOptimizer_BakeNonAnimatedBlendshapes", true);
            config.KeepMMDBlendshapes = EditorPrefs.GetBool("VRCAvatarOptimizer_KeepMMDBlendshapes", true);
            config.DeleteUnusedGameObjects = EditorPrefs.GetBool("VRCAvatarOptimizer_DeleteUnusedGameObjects", true);
            config.MergeSiblingPhysBones = EditorPrefs.GetBool("VRCAvatarOptimizer_MergeSiblingPhysBones", true);
            config.CleanExpressionParametersWhenOverBudget = EditorPrefs.GetBool("VRCAvatarOptimizer_CleanExpressionParametersWhenOverBudget", true);
            config.ForceCleanExpressionParameters = EditorPrefs.GetBool("VRCAvatarOptimizer_ForceCleanExpressionParameters", false);
            config.FixRendererBounds = EditorPrefs.GetBool("VRCAvatarOptimizer_FixRendererBounds", true);
            config.AnchorProbesToHips = EditorPrefs.GetBool("VRCAvatarOptimizer_AnchorProbesToHips", true);
            config.AtlasMaterials = EditorPrefs.GetBool("VRCAvatarOptimizer_AtlasMaterials", false);
            config.UnmapJawBone = EditorPrefs.GetBool("VRCAvatarOptimizer_UnmapJawBone", false);
            config.EnableLegacyBlendShapeNormals = EditorPrefs.GetBool("VRCAvatarOptimizer_EnableLegacyBlendShapeNormals", false);
            config.ReplaceShaders = EditorPrefs.GetBool("VRCAvatarOptimizer_ReplaceShaders", true);
            config.OptimizeTextures = EditorPrefs.GetBool("VRCAvatarOptimizer_OptimizeTextures", true);
            config.DecimateMeshes = EditorPrefs.GetBool("VRCAvatarOptimizer_DecimateMeshes", true);
            config.RemoveIncompatibleComponents = EditorPrefs.GetBool("VRCAvatarOptimizer_RemoveIncompatibleComponents", true);
            config.SkipDryRunBundleBuild = EditorPrefs.GetBool("VRCAvatarOptimizer_SkipDryRunBundleBuild", false);
            config.MaxSizeConvergenceAttempts = EditorPrefs.GetInt("VRCAvatarOptimizer_MaxSizeConvergenceAttempts", 3);
            config.DeletePlacementLocationBeforeConversion = EditorPrefs.GetBool("VRCAvatarOptimizer_DeletePlacementLocationBeforeConversion", false);
            config.DeleteExistingTargetGameObjects = EditorPrefs.GetBool("VRCAvatarOptimizer_DeleteExistingTargetGameObjects", false);
            config.ClearEditorLogBeforeConversion = EditorPrefs.GetBool("VRCAvatarOptimizer_ClearEditorLogBeforeConversion", false);
            cachedProfile = PlatformProfile.GetProfile(config.Platform, config.TargetRank);
        }

        private void SavePreferences()
        {
            Debug.Log("[VRCAvatarOptimizerWindow] SavePreferences called.");
            EditorPrefs.SetInt("VRCAvatarOptimizer_Platform", (int)config.Platform);
            EditorPrefs.SetInt("VRCAvatarOptimizer_TargetRank", (int)config.TargetRank);
            EditorPrefs.SetInt("VRCAvatarOptimizer_PlacementLocation", (int)config.PlacementLocation);
            EditorPrefs.SetInt("VRCAvatarOptimizer_PruningStrategy", (int)config.PruningStrategy);
            EditorPrefs.SetBool("VRCAvatarOptimizer_DuplicateAvatar", config.DuplicateAvatar);
            EditorPrefs.SetBool("VRCAvatarOptimizer_AddPlatformSuffixes", config.AddPlatformSuffixes);
            EditorPrefs.SetBool("VRCAvatarOptimizer_RemapAnimationsAndVRCFury", config.RemapAnimationsAndVRCFury);
            EditorPrefs.SetBool("VRCAvatarOptimizer_OptimizeFXLayer", config.OptimizeFXLayer);
            EditorPrefs.SetBool("VRCAvatarOptimizer_UseNaNimationToggles", config.UseNaNimationToggles);
            EditorPrefs.SetBool("VRCAvatarOptimizer_BakeNonAnimatedBlendshapes", config.BakeNonAnimatedBlendshapes);
            EditorPrefs.SetBool("VRCAvatarOptimizer_KeepMMDBlendshapes", config.KeepMMDBlendshapes);
            EditorPrefs.SetBool("VRCAvatarOptimizer_DeleteUnusedGameObjects", config.DeleteUnusedGameObjects);
            EditorPrefs.SetBool("VRCAvatarOptimizer_MergeSiblingPhysBones", config.MergeSiblingPhysBones);
            EditorPrefs.SetBool("VRCAvatarOptimizer_CleanExpressionParametersWhenOverBudget", config.CleanExpressionParametersWhenOverBudget);
            EditorPrefs.SetBool("VRCAvatarOptimizer_ForceCleanExpressionParameters", config.ForceCleanExpressionParameters);
            EditorPrefs.SetBool("VRCAvatarOptimizer_FixRendererBounds", config.FixRendererBounds);
            EditorPrefs.SetBool("VRCAvatarOptimizer_AnchorProbesToHips", config.AnchorProbesToHips);
            EditorPrefs.SetBool("VRCAvatarOptimizer_AtlasMaterials", config.AtlasMaterials);
            EditorPrefs.SetBool("VRCAvatarOptimizer_UnmapJawBone", config.UnmapJawBone);
            EditorPrefs.SetBool("VRCAvatarOptimizer_EnableLegacyBlendShapeNormals", config.EnableLegacyBlendShapeNormals);
            EditorPrefs.SetBool("VRCAvatarOptimizer_ReplaceShaders", config.ReplaceShaders);
            EditorPrefs.SetBool("VRCAvatarOptimizer_OptimizeTextures", config.OptimizeTextures);
            EditorPrefs.SetBool("VRCAvatarOptimizer_DecimateMeshes", config.DecimateMeshes);
            EditorPrefs.SetBool("VRCAvatarOptimizer_RemoveIncompatibleComponents", config.RemoveIncompatibleComponents);
            EditorPrefs.SetBool("VRCAvatarOptimizer_SkipDryRunBundleBuild", config.SkipDryRunBundleBuild);
            EditorPrefs.SetInt("VRCAvatarOptimizer_MaxSizeConvergenceAttempts", config.MaxSizeConvergenceAttempts);
            EditorPrefs.SetBool("VRCAvatarOptimizer_DeletePlacementLocationBeforeConversion", config.DeletePlacementLocationBeforeConversion);
            EditorPrefs.SetBool("VRCAvatarOptimizer_DeleteExistingTargetGameObjects", config.DeleteExistingTargetGameObjects);
            EditorPrefs.SetBool("VRCAvatarOptimizer_ClearEditorLogBeforeConversion", config.ClearEditorLogBeforeConversion);
        }

        private void OnGUI()
        {
            guiStopwatch.Restart();
            EventType currentEventType = Event.current != null ? Event.current.type : EventType.Ignore;

            var sw = System.Diagnostics.Stopwatch.StartNew();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Avatar Optimizer (VRChat)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Optimize VRChat avatars for target platforms (PC & Android) according to SDK performance rank limits. Automatically duplicates materials, remaps VRCFury toggles & material swaps, optimizes texture memory budgets, decimates meshes, and prunes PhysBones to hit target performance ranks.", MessageType.Info);
            EditorGUILayout.Space(10);

            long tHeader = sw.ElapsedMilliseconds;

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
                    cachedHasDescriptor = HasAvatarDescriptor(avatarRoot);
                }
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                {
                    avatarRoot = null;
                    cachedHasDescriptor = false;
                }
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            long tSelection = sw.ElapsedMilliseconds - tHeader;

            // Target Performance Level & Options
            EditorGUILayout.LabelField("2. Conversion Preferences", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();

            // Enforce label width to leave at least 50% width for control inputs without overlapping labels
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Math.Max(220f, EditorGUIUtility.currentViewWidth * 0.55f);

            config.Platform = (TargetPlatform)EditorGUILayout.EnumPopup("Target Platform", config.Platform);
            config.TargetRank = (AvatarPerformanceRank)EditorGUILayout.EnumPopup("Target Performance Rank", config.TargetRank);

            if (cachedProfile == null)
            {
                cachedProfile = PlatformProfile.GetProfile(config.Platform, config.TargetRank);
            }

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
            config.OptimizeFXLayer = EditorGUILayout.ToggleLeft("Optimize FX Layer (Direct Blend Tree combining)", config.OptimizeFXLayer);
            config.UseNaNimationToggles = EditorGUILayout.ToggleLeft("Use NaNimation Toggles for Skinned Meshes", config.UseNaNimationToggles);
            if (config.UseNaNimationToggles)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("Lets meshes that are animated on/off still be merged: each gets a zero-weight toggle bone whose scale animates to NaN, discarding its triangles, and its active-state curves are rewritten to drive that bone. Meshes whose vertices already use four bones are left unmerged rather than losing skinning influence. With this off, animated-toggle meshes are excluded from merging entirely.", MessageType.None);
                EditorGUI.indentLevel--;
            }
            config.BakeNonAnimatedBlendshapes = EditorGUILayout.ToggleLeft("Bake Non-Animated Blendshapes", config.BakeNonAnimatedBlendshapes);
            if (config.BakeNonAnimatedBlendshapes)
            {
                EditorGUI.indentLevel++;
                config.KeepMMDBlendshapes = EditorGUILayout.ToggleLeft("Protect MMD & Viseme Facial Blendshapes", config.KeepMMDBlendshapes);
                EditorGUI.indentLevel--;
            }
            config.DeleteUnusedGameObjects = EditorGUILayout.ToggleLeft("Delete Unused & Unweighted GameObjects", config.DeleteUnusedGameObjects);

            config.CleanExpressionParametersWhenOverBudget = EditorGUILayout.ToggleLeft("Clean Expression Parameters When Over Budget", config.CleanExpressionParametersWhenOverBudget);
            if (config.CleanExpressionParametersWhenOverBudget)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("Only acts when the avatar exceeds VRChat's 256-bit synced parameter budget, and only removes parameters nothing references. Stops as soon as the avatar is back under the cap; parameters still in use are never deleted.", MessageType.None);
                config.ForceCleanExpressionParameters = EditorGUILayout.ToggleLeft("Always Clean (even when under budget)", config.ForceCleanExpressionParameters);
                if (config.ForceCleanExpressionParameters)
                    EditorGUILayout.HelpBox("Dead parameters will be removed even when the avatar already fits. Parameters driven only by external tooling (VRCFury, Modular Avatar, OSC) can look unused to static analysis — verify your menus after enabling this.", MessageType.Warning);
                EditorGUI.indentLevel--;
            }
            config.FixRendererBounds = EditorGUILayout.ToggleLeft("Fix Renderer Bounds & Probe Anchors", config.FixRendererBounds);
            if (config.FixRendererBounds)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("Recalculates SkinnedMeshRenderer bounds after merging/atlasing, which otherwise leaves them too tight and makes the avatar cull incorrectly. Recommended.", MessageType.None);
                config.AnchorProbesToHips = EditorGUILayout.ToggleLeft("Anchor Light Probes to Hips", config.AnchorProbesToHips);
                EditorGUI.indentLevel--;
            }

            config.AtlasMaterials = EditorGUILayout.ToggleLeft("Atlas Materials into Shared Textures (experimental)", config.AtlasMaterials);
            if (config.AtlasMaterials)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Packs compatible materials' textures into one atlas and rewrites mesh UVs — the only way below a material slot limit that deduplication cannot reach. " +
                    "Only groups that are provably safe are atlased: identical shader/queue/keywords, identical non-texture properties, UVs inside [0,1], and no vertices shared between submeshes. Everything else is skipped with a logged reason.",
                    MessageType.None);
                EditorGUILayout.HelpBox("Visually destructive and not reversible by re-running the optimizer. Verify the result before uploading.", MessageType.Warning);
                EditorGUI.indentLevel--;
            }

            config.ReplaceShaders = EditorGUILayout.ToggleLeft("Replace Shaders with Mobile Shaders", config.ReplaceShaders);
            config.OptimizeTextures = EditorGUILayout.ToggleLeft("Optimize Texture Memory Budget", config.OptimizeTextures);
            if (config.OptimizeTextures)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("Automatically optimizes resolution, format, and compression per texture to hit VRAM and AssetBundle budget caps.", MessageType.None);
                EditorGUI.indentLevel--;
            }

            config.MergeSiblingPhysBones = EditorGUILayout.ToggleLeft("Merge Sibling PhysBone Chains", config.MergeSiblingPhysBones);
            if (config.MergeSiblingPhysBones)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("Collapses sibling PhysBones with identical settings into one component rooted at their shared parent (using Ignore Transforms), costing 1 extra affected transform per merge. Runs before pruning so chains are consolidated rather than deleted.", MessageType.None);
                EditorGUI.indentLevel--;
            }
            config.PruningStrategy = (PhysBonePruningStrategy)EditorGUILayout.EnumPopup("PhysBone Pruning Strategy", config.PruningStrategy);
            config.DecimateMeshes = EditorGUILayout.ToggleLeft("Decimate Meshes to Poly Limit", config.DecimateMeshes);

            config.UnmapJawBone = EditorGUILayout.ToggleLeft("Unmap Humanoid Jaw Bone", config.UnmapJawBone);
            config.EnableLegacyBlendShapeNormals = EditorGUILayout.ToggleLeft("Enable Legacy Blend Shape Normals", config.EnableLegacyBlendShapeNormals);
            if (config.UnmapJawBone || config.EnableLegacyBlendShapeNormals)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("These edit the shared model importer, so they affect every avatar using that FBX — not just the optimized clone. Neither is required for a successful upload.", MessageType.Warning);
                EditorGUI.indentLevel--;
            }
            config.RemoveIncompatibleComponents = EditorGUILayout.ToggleLeft("Remove Incompatible Components (SDK Auto Fix can do this)", config.RemoveIncompatibleComponents);
            if (!config.RemoveIncompatibleComponents)
            {
                EditorGUILayout.HelpBox(
                    "Off (recommended): the VRC SDK panel's Auto Fix removes illegal components, converts DynamicBones → PhysBones, and converts Unity constraints → VRC constraints (conversion preserves behavior — this pass would just delete them). " +
                    "Note: during the dry-run size verification the incompatible components are always removed temporarily (and restored afterwards via Undo) so the measured bundle size matches an SDK-auto-fixed upload.",
                    MessageType.None
                );
            }

            config.SkipDryRunBundleBuild = EditorGUILayout.ToggleLeft("Skip Dry-Run Bundle Build (Step 8.5)", config.SkipDryRunBundleBuild);
            if (config.SkipDryRunBundleBuild)
            {
                EditorGUILayout.HelpBox(
                    "The compressed avatar size will NOT be verified with a real SDK build — only Step 5's fast-math texture estimate is used. Faster conversions, but the summary won't show a verified bundle size.",
                    MessageType.Warning
                );
            }
            else
            {
                EditorGUI.indentLevel++;
                config.MaxSizeConvergenceAttempts = EditorGUILayout.IntSlider("Max Size Convergence Retries", config.MaxSizeConvergenceAttempts, 0, 6);
                EditorGUILayout.HelpBox(
                    config.MaxSizeConvergenceAttempts == 0
                        ? "Retries disabled: if the built bundle exceeds the platform cap it is reported as an error without further compression."
                        : $"If the built bundle exceeds the platform cap, the texture budget is tightened by the measured overshoot and rebuilt, up to {config.MaxSizeConvergenceAttempts} time(s). Each retry costs one full SDK build; the loop stops early once it fits or when the bundle stops shrinking.",
                    MessageType.None
                );
                EditorGUI.indentLevel--;
            }

            EditorGUIUtility.labelWidth = prevLabelWidth;

            if (EditorGUI.EndChangeCheck())
            {
                cachedProfile = PlatformProfile.GetProfile(config.Platform, config.TargetRank);
                SavePreferences();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            long tPrefs = sw.ElapsedMilliseconds - tHeader - tSelection;

            // Avatar Descriptor validation
            if (avatarRoot != null && !cachedHasDescriptor)
            {
                EditorGUILayout.HelpBox($"'{avatarRoot.name}' has no VRC Avatar Descriptor component. Select the avatar root GameObject.", MessageType.Error);
            }

            // Action Button
            EditorGUI.BeginDisabledGroup(isConverting || avatarRoot == null || !cachedHasDescriptor);

            if (GUILayout.Button($"Optimize Avatar for {config.Platform}", GUILayout.Height(38)))
            {
                EditorApplication.delayCall += StartConversion;
            }

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(10);

            long tButton = sw.ElapsedMilliseconds - tHeader - tSelection - tPrefs;

            // Summary Results
            if (summary != null)
            {
                summary.RenderGUI();
            }

            EditorGUILayout.EndScrollView();

            sw.Stop();
            long totalMs = sw.ElapsedMilliseconds;
            if (totalMs > 2)
            {
                Debug.LogWarning($"[VRCAvatarOptimizerWindow] OnGUI ({currentEventType}) Total: {totalMs} ms | Header: {tHeader} ms | Selection: {tSelection} ms | Prefs: {tPrefs} ms | Button: {tButton} ms | Summary/End: {totalMs - tHeader - tSelection - tPrefs - tButton} ms");
            }
        }

        private void HandleDragAndDrop(Rect dropArea)
        {
            Event currentEvent = Event.current;

            switch (currentEvent.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                {
                    if (!dropArea.Contains(currentEvent.mousePosition)) return;

                    // Only advertise Copy for something we can actually accept, otherwise the cursor
                    // promises a drop that then silently does nothing.
                    GameObject candidate = DragAndDrop.objectReferences.OfType<GameObject>().FirstOrDefault();
                    DragAndDrop.visualMode = candidate != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

                    if (currentEvent.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        if (candidate != null)
                        {
                            avatarRoot = candidate;
                            cachedHasDescriptor = HasAvatarDescriptor(avatarRoot);
                        }
                    }

                    // Consume BOTH events. Previously only DragPerform was consumed, so the DragUpdated
                    // event kept bubbling: the drag was never registered as handled by this area, drops
                    // could be dropped on the floor, and the unmatched drag left UIElements' pointer
                    // state inconsistent — which is what raises the bare
                    // "Assertion failed ... PointerDeviceState:ReleaseButton" on DragExited.
                    currentEvent.Use();
                    break;
                }

                case EventType.DragExited:
                    // Acknowledge the drag ending over this window so the pointer state is released cleanly.
                    if (dropArea.Contains(currentEvent.mousePosition)) currentEvent.Use();
                    break;
            }
        }

        private static bool HasAvatarDescriptor(GameObject go)
        {
            return go != null && go.GetComponents<Component>().Any(c => c != null && c.GetType().Name.Contains("AvatarDescriptor"));
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
