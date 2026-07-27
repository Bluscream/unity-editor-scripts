# VRChat Avatar Optimizer (`dev.bluscream.avataroptimizer_vrc`)

Automated, platform-aware avatar conversion and optimization pipeline targeting PC and Mobile (Android/Quest, iOS) performance ranks for VRChat.

## Overview

`dev.bluscream.avataroptimizer_vrc` automates cross-platform avatar optimization by downscaling textures, re-encoding materials to mobile-compliant shaders, pruning excess PhysBones, Contacts, Constraints, and particles, stripping mobile-incompatible components, decimating meshes, and rewriting animator controllers / animation clips / VRCFury components to point at the optimized materials.

---

## Editor Window

`Bluscream/VRChat/Avatar Optimizer` — GUI dashboard for selecting the target platform and rank profile, previewing the avatar's current rating estimate, tuning texture/crunch budgets, and executing the conversion. The selected avatar root must have a VRC Avatar Descriptor.

---

## API Reference

### 1. `VRCAvatarOptimizerCore` (`VRCAvatarOptimizerCore.cs`)

Core pipeline driver for platform conversion and optimization.

#### `ConversionConfig`
- `Platform`: `TargetPlatform` (`PC`, `Android`, `iOS`)
- `TargetRank`: `AvatarPerformanceRank` (`Excellent`, `Good`, `Medium`, `Poor`, `VeryPoor`)
- `PlacementLocation`: `AssetPlacementLocation` (`SeparateFolder` → `Assets/_AVATAROPTIMIZER/<TargetAvatarName>/`, `SameFolderAsOriginal`)
- `PruningStrategy`: `PhysBonePruningStrategy` (`Disabled`, `DeepestFirst`, `InteractiveChecklist` — a modal checklist pre-selecting the deepest-first suggestion)
- `DuplicateAvatar`: `bool` — clone the avatar instead of editing in place
- `AddPlatformSuffixes`: `bool` — rename clone to `<Name> (<Platform>) [<Rank>]`
- `AvatarSuffix`: `string` — custom clone suffix when `AddPlatformSuffixes` is off (`null` = none)
- `RemoveIncompatibleComponents`, `ReplaceShaders`, `OptimizeTextures`, `DecimateMeshes`, `RemapAnimationsAndVRCFury`: `bool` pipeline toggles
- `MaxTextureResolution`: `int` (128–4096)
- `CrunchCompressionQuality`: `int` (0 = no crunch/raw ASTC … 100 = max crunch)
- `UncompressedAvatarHeadroomMB` / `CompressedAvatarHeadroomMB`: `float` — headroom reserved for mesh/animation payload inside the VRAM / bundle-size budgets
- `CrunchStepPercent`: `int` — granularity of the crunch quality ladder
- `DeletePlacementLocationBeforeConversion`, `DeleteExistingTargetGameObjects`, `ClearEditorLogBeforeConversion`: `bool`

#### Methods
- `ConvertAvatar(GameObject avatarRoot, ConversionConfig config, Action<string, float> progressCallback = null)`: `ConversionSummary`
  - Runs the full pipeline (build-target switch → duplicate → component removal → material/shader remap → animation rewrite → texture budget → PhysBone pruning → decimation → slot/mesh consolidation → platform rules → bundle-size verification). Throws `OperationCanceledException` when canceled via the progress callback.
- `GetTargetAvatarName(string sourceName, ConversionConfig config, PlatformProfile profile)`: `string`
- `GetPlacementFolder(string targetAvatarName, AssetPlacementLocation location)`: `string`
- `StripPlatformSuffix(string avatarName)`: `string`
- `SwitchBuildTargetIfNeeded(TargetPlatform platform)`
- `ClearEditorLog()` — truncates `Editor.log` (Windows/macOS/Linux)

---

### 2. Platform Profiles (`PlatformProfiles/`)

`PlatformProfile.GetProfile(TargetPlatform, AvatarPerformanceRank)` returns the quantitative limits for a platform/rank combination (triangles, skinned meshes, material slots, bones, texture memory, PhysBones/colliders/collision checks, contacts, constraints, particles, renderers, cloth, physics, lights, audio, bounds, asset-bundle size). All rank limits are verified against the VRChat SDK's `StatsLevels` assets; the compressed bundle size cap (10 MB mobile / 200 MB PC) is read live from `VRC.ValidationHelpers` when the SDK is present.

Profiles also carry behavior:
- `BlacklistedComponentNames` / `WhitelistedComponentNames` — per-platform component rules
- `ShouldRemoveComponentCustom(Component)` — e.g. mobile profiles remove Cameras, Joints, DynamicBones, FinalIK, post-processing, and non-VRC constraints
- `ExecutePlatformConversions(GameObject, ...)` — e.g. mobile pixel-light clamp
- `ValidatePlatformRules(GameObject, ConversionSummary)` — bounds & material-slot warnings

iOS profiles inherit all Android/Quest limits and rules.

---

### 3. Pipeline Modules

- `AvatarComponentRemover` — strips blacklisted/incompatible components (dependency-aware, multi-pass) and prunes excess Animators, Lights, AudioSources, and Cloth.
- `AvatarPhysBonePruner` — prunes PhysBone components/colliders and trims collider lists to meet component, transform, collider, and collision-check limits using the configured strategy.
- `AvatarContactOptimizer` / `AvatarConstraintOptimizer` — prune excess VRCContact / constraint components.
- `AvatarParticleOptimizer` — prunes particle systems and trail/line renderers, caps particle counts and mesh-particle polygons, disables forbidden trail/collision modules.
- `AvatarMaterialSlotOptimizer` — deduplicates identical materials and consolidates duplicate submesh slots.
- `AvatarMeshCountOptimizer` — combines same-parent static meshes when over the renderer limit (keeps GameObjects, persists combined meshes).
- `ShaderMapping` / `ShaderPropertyMapper` — maps Poiyomi/lilToon/Standard/etc. shaders to `VRChat/Mobile/*` shaders and transfers compatible properties (mappings configurable via `Editor/Resources/ShaderPropertyMappings.json`).
- `AvatarAnimationRewriter` — duplicates and rewrites AnimatorControllers, AnimatorOverrideControllers, AnimationClips (material-swap curves), and VRCFury component references to use the optimized materials.
- `ConversionSummary` — before/after stats table, warnings/errors list, and console report rendered against the target profile's limits.
