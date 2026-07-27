# VRChat Mobile Decimater (`dev.bluscream.mobiledecimater_vrc`)

Automatic mesh decimation processor and scene build hook for optimizing VRChat Mobile (Android / iOS) avatars.

## Overview

`dev.bluscream.mobiledecimater_vrc` hooks into Unity scene builds (`IProcessSceneWithReport`) to automatically reduce polygon counts for mobile platform target builds, preserving blendshapes, UVs, and boundary edges.

---

## API Reference

### 1. `MobileDecimater` Component (`MobileDecimater.cs`)

Attach to any GameObject with a `MeshFilter` or `SkinnedMeshRenderer` to configure per-mesh decimation rules.

#### Public Fields & Settings
- `targetTriangleCount`: `int` (Default: `0` - Use `decimationRatio` if `<= 0`)
- `decimationRatio`: `float` (Range: `0.01f` - `1.0f`, Default: `0.5f`)
- `preserveBlendShapes`: `bool` (Default: `true`)
- `preserveBoundary`: `bool` (Default: `true`)
- `preventIntersection`: `bool` (Default: `true`)
- `targetMetric`: `float` (Default: `0.1f`)

---

### 2. `MobileDecimationProcessor` (`Editor/MobileDecimationProcessor.cs`)

Editor processor executing mesh decimation on scene builds and exposing avatar polygon budget APIs.

#### Implemented Interfaces
- `IProcessSceneWithReport` (`callbackOrder = 0`)

#### Public Static API
- `DecimateAvatarMeshesToTargetTris(GameObject avatarRoot, int targetTriangles, Action<string> progressCallback = null)`: `int`
  - Decimates all meshes on the avatar proportionally to fit within specified total triangle count budget.
  - Returns the final total triangle count.
