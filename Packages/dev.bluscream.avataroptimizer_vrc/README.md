# VRChat Avatar Optimizer (`dev.bluscream.avataroptimizer_vrc`)

Automated, platform-aware avatar conversion and optimization pipeline targeting PC and Mobile (Android/Quest, iOS) performance ranks for VRChat.

## Overview

Downscales textures, re-encodes materials to mobile-compliant shaders, prunes and consolidates PhysBones, Contacts, Constraints and particles, strips mobile-incompatible components, decimates meshes, and rewrites animator controllers / animation clips / VRCFury references to point at the optimized assets.

See [`PURPOSE.md`](PURPOSE.md) for the design intent, the full pipeline order, configuration precedence, and current known gaps.

---

## Editor Window

`Bluscream/VRChat/Avatar Optimizer` — select a target platform and rank profile, toggle individual passes, and run the conversion. Takes one avatar at a time via an "Avatar Root" field; the avatar must have a VRC Avatar Descriptor. All settings persist to `EditorPrefs`.

When `PhysBonePruningStrategy.InteractiveChecklist` is selected, a modal checklist opens during the run with the deepest-first suggestion pre-ticked, so the automatic choice can be overridden.

---

## API Reference

### `VRCAvatarOptimizerCore`

Core pipeline driver.

#### `ConversionConfig`

**Target**
- `Platform`: `TargetPlatform` (`PC`, `Android`, `iOS`)
- `TargetRank`: `AvatarPerformanceRank` (`Excellent`, `Good`, `Medium`, `Poor`, `VeryPoor`)
- `PlacementLocation`: `AssetPlacementLocation` (`SeparateFolder` → `Assets/_AVATAROPTIMIZER/<TargetAvatarName>/`, `SameFolderAsOriginal`)

**Cloning**
- `DuplicateAvatar` (`true`) — clone the avatar instead of editing in place
- `AddPlatformSuffixes` (`true`) — rename the clone to `<Name> (<Platform>) [<Rank>]`
- `AvatarSuffix` (`null`) — custom clone suffix; `null` uses the profile's. Not exposed in the window.
- `DeletePlacementLocationBeforeConversion`, `DeleteExistingTargetGameObjects`, `ClearEditorLogBeforeConversion` (all `false`)

**Passes — on by default**
- `ReplaceShaders` — remap materials to `VRChat/Mobile/*`
- `OptimizeTextures` — automatic resolution/format/crunch allocation against the VRAM and bundle budgets
- `DecimateMeshes` — decimate to the triangle budget
- `RemapAnimationsAndVRCFury` — clone and rewrite controllers, clips and VRCFury references
- `OptimizeFXLayer` — collapse two-state toggle layers into a Direct Blend Tree
- `BakeNonAnimatedBlendshapes` / `KeepMMDBlendshapes` — strip unanimated blendshapes, whitelisting MMD morphs and visemes
- `DeleteUnusedGameObjects` — remove component-less, unreferenced transforms
- `MergeSiblingPhysBones` — collapse sibling PhysBone chains into one component rooted at their shared parent
- `CleanExpressionParametersWhenOverBudget` — remove dead synced parameters, but only when over VRChat's cap
- `FixRendererBounds` / `AnchorProbesToHips` — recalculate bounds and set a common probe anchor after merging
- `UseNaNimationToggles` — merge separately-toggled meshes using NaN-scaled toggle bones

**Passes — opt-in**
- `RemoveIncompatibleComponents` (`false`) — the SDK panel's Auto Fix converts rather than deletes, so this destructive pass is off
- `AtlasMaterials` (`false`) — pack compatible materials into a shared texture atlas and rewrite UVs. Visually destructive and irreversible.
- `ForceCleanExpressionParameters` (`false`) — clean dead parameters even when already under budget
- `UnmapJawBone`, `EnableLegacyBlendShapeNormals` (`false`) — edit the shared model importer, affecting every avatar using that FBX

**Bundle verification**
- `SkipDryRunBundleBuild` (`false`) — skip the Step 8.5 dry-run builds and rely on the Step 5 estimate
- `MaxSizeConvergenceAttempts` (`3`) — how many times the texture budget may tighten and rebuild

Texture tuning has no user-facing fields; resolution, format, crunch and budgets are all derived from the profile's caps and refined against measured builds. See `VRCAvatarOptimizerCore.TextureAutoTuning`.

#### Methods
- `ConvertAvatar(GameObject, ConversionConfig, Action<string, float>)`: `ConversionSummary` — runs the full pipeline. Throws `OperationCanceledException` when cancelled via the progress callback.
- `GetTargetAvatarName(string, ConversionConfig, PlatformProfile)`: `string`
- `GetPlacementFolder(string, AssetPlacementLocation)`: `string`
- `StripPlatformSuffix(string)`: `string`
- `SanitizeAvatarDescriptors(GameObject)` — enforce exactly one descriptor on the root
- `SwitchBuildTargetIfNeeded(TargetPlatform)`
- `ClearEditorLog()` — truncates `Editor.log` (Windows/macOS/Linux)

---

### Platform Profiles (`PlatformProfiles/`)

`PlatformProfile.GetProfile(TargetPlatform, AvatarPerformanceRank)` returns the limits for a platform/rank pair. Precedence, weakest to strongest: hardcoded defaults → `config.json` → live VRChat SDK. Compressed bundle caps (10 MB mobile / 200 MB PC) come from `VRC.ValidationHelpers` when the SDK is present.

Profiles also carry behavior:
- `ComponentBlacklist` / `ComponentWhitelist` — config-driven per-platform component rules
- `ShouldRemoveComponentCustom(Component)` — type-based checks that cannot be expressed as strings (mobile removes Cameras, Joints, DynamicBones, FinalIK, post-processing, non-VRC constraints)
- `ExecutePlatformConversions(GameObject, ...)` — mobile clamps `QualitySettings.pixelLightCount` to 1
- `ValidatePlatformRules(GameObject, ConversionSummary)` — bounds and material-slot warnings

iOS profiles inherit all Android/Quest limits and rules.

---

### Pipeline Modules (`Optimizers/`)

**Mesh & texture**
- `AvatarMeshCountOptimizer` — merges static MeshRenderers and skinned meshes when over the renderer limits
- `AvatarMaterialSlotOptimizer` — lossless material deduplication and submesh consolidation
- `AvatarBlendShapeOptimizer` — bakes and strips unanimated blendshapes, whitelisting MMD/viseme/blink shapes
- `AvatarTextureAtlaser` + `TextureAtlasPacker` — growing binary-tree atlas packing with UV rewriting *(opt-in)*
- `AvatarBoundsOptimizer` — recalculates `localBounds` and sets probe anchors after merging

**Dynamics**
- `AvatarPhysBonePruner` — enforces component, transform, collider and collision-check budgets
- `AvatarPhysBoneMerger` — collapses sibling chains into one component via `ignoreTransforms`
- `AvatarContactOptimizer` / `AvatarConstraintOptimizer` — prune excess contacts and constraints
- `AvatarParticleOptimizer` — prunes particle systems and trail/line renderers, caps counts, disables forbidden modules
- `AvatarLightOptimizer` — disables excess dynamic lights
- `AvatarPenetratorDetector` — identifies DPS/TPS/SPS renderers so merging leaves them alone

**Animation & parameters**
- `AvatarAnimationRewriter` — clones and rewrites controllers, override controllers, clips and VRCFury references
- `AvatarAnimatorOptimizer` — collapses toggle layers into a Direct Blend Tree, purges dead layers
- `AvatarNaNimationOptimizer` — NaN-scaled toggle bones so separately-toggled meshes can share one renderer
- `AvatarExpressionParameterCleaner` — reclaims the synced parameter budget when over cap

**Other**
- `AvatarComponentRemover` — strips blacklisted components (dependency-aware, multi-pass), prunes excess Animators/Lights/AudioSources/Cloth, deletes unused GameObjects
- `AvatarRigOptimizer` — humanoid jaw unmapping and Legacy Blend Shape Normals *(opt-in)*
- `AvatarBudgetReducers` — `TextureBudgetReducer` and `MeshDecimationReducer` drive the Step 8.5 convergence loop
- `ShaderMapping` / `ShaderPropertyMapper` — shader selection and property transfer (`Editor/Resources/ShaderPropertyMappings.json`)
- `ConversionSummary` — before/after stats, warnings and errors, rendered against the target profile's limits
