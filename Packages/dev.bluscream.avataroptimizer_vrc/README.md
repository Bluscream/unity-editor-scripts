# VRChat Avatar Optimizer (`dev.bluscream.avataroptimizer_vrc`)

Automated, platform-aware avatar conversion and optimization pipeline targeting PC and Mobile (Android / iOS Quest) performance ranks for VRChat.

## Overview

`dev.bluscream.avataroptimizer_vrc` automates cross-platform avatar optimization by downscaling textures, re-encoding materials to mobile-compliant toon shaders, pruning excess PhysBones, Contacts, Constraints, and particles, stripping blacklisted components, and re-writing animation clips.

---

## API Reference

### 1. `VRCAvatarOptimizerCore` (`VRCAvatarOptimizerCore.cs`)

Core pipeline driver for platform conversion and optimization.

#### Data Structures

##### `OptimizerConfig`
- `TargetPlatform`: `TargetPlatform` (`PC`, `Android`, `iOS`)
- `TargetRank`: `AvatarPerformanceRank` (`Excellent`, `Good`, `Medium`, `Poor`, `VeryPoor`)
- `MaxTextureResolution`: `int` (128, 256, 512, 1024, 2048, 4096)
- `UncompressedAvatarHeadroomMB`: `float` (Default: `5.0f`)
- `RemapShaders`: `bool`
- `RemapMaterials`: `bool`
- `PrunePhysBones`: `bool`
- `PruneContacts`: `bool`
- `PruneConstraints`: `bool`
- `StripIncompatibleComponents`: `bool`

#### Methods
- `OptimizeAvatar(GameObject sourceAvatar, OptimizerConfig config, Action<string, float> progressCallback = null)`: `ConversionSummary`
  - Runs full optimization pipeline on avatar duplicate and returns `ConversionSummary`.
- `CreateOptimizedDuplicate(GameObject sourceAvatar, TargetPlatform platform)`: `GameObject`
  - Creates a cleaned duplicate GameObject for platform optimization.

---

### 2. Platform Profiles System (`PlatformProfiles/`)

Defines quantitative performance rank limits for PC and Mobile platforms.

#### Base Class: `PlatformProfile` (`PlatformProfiles/PlatformProfile.cs`)
- `Platform`: `TargetPlatform`
- `Rank`: `AvatarPerformanceRank`
- `MaxTriangles`: `int`
- `MaxSkinnedMeshes`: `int`
- `MaxMeshRenderers`: `int`
- `MaxMaterialSlots`: `int`
- `MaxBones`: `int`
- `MaxAnimators`: `int`
- `MaxBoundsSize`: `Vector3`
- `MaxTextureMemoryBytes`: `long` (e.g. `40 * 1024 * 1024L; // 40 MB`)
- `MaxAssetBundleSizeBytes`: `long` (e.g. `10 * 1024 * 1024L; // 10 MB`)
- `MaxPhysBoneComponents`: `int`
- `MaxPhysBoneTransforms`: `int`
- `MaxPhysBoneColliders`: `int`
- `MaxPhysBoneCollisionChecks`: `int`
- `MaxContacts`: `int`
- `MaxConstraints`: `int`
- `MaxConstraintDepth`: `int`
- `MaxParticleSystems`: `int`
- `MaxActiveParticles`: `int`
- `MaxMeshParticlePolyCount`: `int`
- `ParticleTrailsEnabledAllowed`: `bool`
- `ParticleCollisionEnabledAllowed`: `bool`
- `MaxTrailRenderers`: `int`
- `MaxLineRenderers`: `int`
- `MaxRaycasts`: `int`
- `MaxClothComponents`: `int`
- `MaxClothVertices`: `int`
- `MaxPhysicsColliders`: `int`
- `MaxRigidbodies`: `int`
- `MaxLights`: `int`
- `MaxAudioSources`: `int`

#### Concrete Profile Classes
- **PC Profiles**: `PC/Excellent.cs`, `PC/Good.cs`, `PC/Medium.cs`, `PC/Poor.cs`, `PC/VeryPoor.cs`
- **Android Profiles**: `Android/Excellent.cs`, `Android/Good.cs`, `Android/Medium.cs`, `Android/Poor.cs`, `Android/VeryPoor.cs`
- **iOS Profiles**: `iOS/Excellent.cs`, `iOS/Good.cs`, `iOS/Medium.cs`, `iOS/Poor.cs`, `iOS/VeryPoor.cs`

---

### 3. Pipeline Processing Modules

- `AvatarComponentRemover` (`AvatarComponentRemover.cs`): Strips blacklisted components and prunes excess Contacts, Constraints, Trail/Line renderers, and Rigidbodies.
- `AvatarPhysBonePruner` (`AvatarPhysBonePruner.cs`): Calculates PhysBone transform trees and prunes excess components/colliders to fit target profile limits.
- `ShaderMapping` & `ShaderPropertyMapper` (`ShaderMapping.cs`, `ShaderPropertyMapper.cs`): Remaps Poiyomi/LilToon/Standard materials to VRChat Mobile Toon shaders while mapping property values.
- `AvatarAnimationRewriter` (`AvatarAnimationRewriter.cs`): Rewrites animator controllers and animation clips when components or transform paths are modified during optimization.

---

## Editor Windows

- `VRCAvatarOptimizerWindow` (`Tools/Bluscream/VRChat/Avatar Optimizer`): GUI dashboard for selecting target platform, rank profile, previewing SDK alerts, and executing avatar conversion.
