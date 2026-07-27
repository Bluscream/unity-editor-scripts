# Bluscream Common VRChat (`dev.bluscream.common_vrc`)

Common VRChat SDK utilities, pre/post build and upload pipeline hooks, avatar statistics evaluation, and validation alert extraction.

## Overview

`dev.bluscream.common_vrc` provides a unified engine for interacting with the VRChat SDK. It includes build & upload pipeline hooks with priority ordering and abort capabilities, full avatar performance stats evaluation, structured SDK validation alert extraction, and helper utilities.

---

## API Reference

### 1. `VRCBuildPipelineHookManager` (`VRCBuildPipelineHookManager.cs`)

Central pipeline manager for intercepting, executing, and aborting VRChat SDK build and upload events.

#### Struct: `HookResult`
- `Success`: `bool` - Indicates if the hook executed cleanly.
- `Abort`: `bool` - If `true`, stops pipeline execution immediately.
- `ErrorMessage`: `string` - Explanation message if aborted.
- `ShowDialog`: `bool` - Whether an Editor utility dialog is presented to the user when aborted.
- `HookResult.Pass()`: Returns a successful result (`Abort = false`).
- `HookResult.Cancel(string message = null, bool showDialog = true)`: Returns an abort result (`Abort = true`).

#### Registration Methods
- `RegisterPreprocessHook(Func<GameObject, HookResult> callback, int priority = 0, string consumerName = null)`
- `RegisterPostprocessHook(Func<GameObject, HookResult> callback, int priority = 0, string consumerName = null)`
- `RegisterBuildRequestedHook(Func<VRCSDKRequestedBuildType, HookResult> callback, int priority = 0, string consumerName = null)`
- `RegisterPreBuildHook(Func<(GameObject avatarRoot, string bundlePath), HookResult> callback, int priority = 0, string consumerName = null)`
- `RegisterPostBuildHook(Func<(GameObject avatarRoot, string bundlePath), HookResult> callback, int priority = 0, string consumerName = null)`
- `RegisterPreUploadHook(Func<(GameObject avatarRoot, string thumbnailPath), HookResult> callback, int priority = 0, string consumerName = null)`
- `RegisterPostUploadHook(Func<(GameObject avatarRoot, string thumbnailPath), HookResult> callback, int priority = 0, string consumerName = null)`

#### Invocation & Abort API
- `InvokePreprocessAvatar(GameObject avatarRoot)`: `HookResult`
- `InvokePostprocessAvatar(GameObject avatarRoot)`: `HookResult`
- `InvokeBuildRequested(VRCSDKRequestedBuildType buildType)`: `HookResult`
- `InvokePreBuild(GameObject avatarRoot, string bundlePath = null)`: `HookResult`
- `InvokePostBuild(GameObject avatarRoot, string bundlePath)`: `HookResult`
- `InvokePreUpload(GameObject avatarRoot, string thumbnailPath = null)`: `HookResult`
- `InvokePostUpload(GameObject avatarRoot, string thumbnailPath = null)`: `HookResult`

---

### 2. `AvatarSDKEvaluator` (`AvatarSDKEvaluator.cs`)

Calculates avatar metrics via VRChat SDK reflection or fallback traversal, and extracts structured validation alerts.

#### Data Structures

##### `AvatarStats`
- `TriangleCount`: `int`
- `SkinnedMeshCount`: `int`
- `MeshRendererCount`: `int`
- `MaterialSlotCount`: `int`
- `PhysBoneComponentCount`: `int`
- `PhysBoneTransformCount`: `int`
- `PhysBoneColliderCount`: `int`
- `PhysBoneCollisionCheckCount`: `int`
- `ContactCount`: `int`
- `ConstraintCount`: `int`
- `ConstraintDepth`: `int`
- `ParticleSystemCount`: `int`
- `ActiveParticleCount`: `int`
- `MeshParticlePolyCount`: `int`
- `TrailRendererCount`: `int`
- `LineRendererCount`: `int`
- `ClothCount`: `int`
- `ClothVertexCount`: `int`
- `LightCount`: `int`
- `AudioSourceCount`: `int`
- `TotalTextureMemoryBytes`: `long`
- `RatingName`: `string` ("Excellent", "Good", "Medium", "Poor", "Very Poor")

##### `SDKAlert`
- `Severity`: `AlertSeverity` (`Info`, `Warning`, `Error`, `BlockingError`)
- `Category`: `string`
- `Message`: `string`
- `TargetObject`: `UnityEngine.Object`

#### Methods
- `EvaluateAvatar(GameObject avatarRoot)`: `AvatarStats`
  - Calculates comprehensive avatar statistics.
- `GetSDKAlerts(GameObject avatarRoot)`: `List<SDKAlert>`
  - Retrieves all active VRChat SDK validation warnings, errors, and hard-cap violations.
- `PrintSDKAlertsToConsole(GameObject avatarRoot, AvatarStats stats = null)`: `void`
  - Outputs a colorized validation report to the Unity Editor Console.
- `BuildAvatarAssetBundle(GameObject avatarRoot, out string bundlePath)`: `long`
  - Performs dry-run build of avatar AssetBundle and returns size in bytes.

---

### 3. `VRCAvatarHelper` (`VRCAvatarHelper.cs`)

#### Methods
- `GetAvatarDescriptor(GameObject avatarRoot)`: `VRC_AvatarDescriptor`
- `GetPipelineManager(GameObject avatarRoot)`: `Component`
- `GetBlueprintID(GameObject avatarRoot)`: `string`
- `SetBlueprintID(GameObject avatarRoot, string blueprintId)`: `bool`
- `ClearBlueprintID(GameObject avatarRoot)`: `bool`
- `FindPhysBones(GameObject avatarRoot)`: `List<Component>`
- `FindPhysBoneColliders(GameObject avatarRoot)`: `List<Component>`
- `FindContacts(GameObject avatarRoot)`: `List<Component>`
- `FindConstraints(GameObject avatarRoot)`: `List<Component>`
- `GetTotalMaterialSlotCount(GameObject avatarRoot)`: `int`
- `GetTotalPolygonCount(GameObject avatarRoot)`: `int`
- `IsMobilePlatformActive()`: `bool`
- `IsPCPlatformActive()`: `bool`

---

### 4. `VRCCommonHelper` (`VRCCommonHelper.cs`)

- `VRC_COMMON_VERSION`: `string` ("1.1.0")
- `IsVRCSDKAvailable()`: `bool`
- `OpenVRCControlPanel()`: `void`
- `GetSelectedAvatarInEditor()`: `GameObject`
- `SwitchBuildTarget(BuildTarget target)`: `bool`
