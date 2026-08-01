# Description

This Unity editor package (`dev.bluscream.avataroptimizer_vrc`) provides automated, platform-aware VRChat avatar optimization. It allows any avatar to be cut, decimated, pruned, and re-encoded to fit user-selected Platform + Performance Rank limits (read directly from the VRChat SDK or from `Packages/dev.bluscream.avataroptimizer_vrc/config.json`). 

It utilizes all known and new optimization techniques to keep metrics strictly under target limits while preserving maximum visual fidelity by staying as close to those limits as possible.

It takes inspiration from existing tools in `.references/` and offers optional, opt-in optimizations beyond VRChat SDK requirements.

Default settings for new presets are configurable via Unity Preferences (`EditorPrefs`) or a dedicated "Avatar Optimizer Settings" window.

The ultimate goal is a **one-click upload readiness** pipeline after selecting a preset.

## Third-Party Addon & Ecosystem Compatibility
Optimizations automatically handle and remap dependencies for:
- VRCFury
- Modular Avatar
- SPS / DPS
- MMD Blendshapes & Animation Clips

# UI / UX

- **Avatar Presets**: Avatar root GameObjects can hold one or more `Avatar Optimizer` components specifying preset settings (platform, rank target, pruning strategies) with a collapsed "Advanced Settings" section for manual limit overrides. Includes a direct "Run Optimizations" button.
- **Hierarchy Context Menu**: Right-click menu on root avatar GameObjects (`Run Optimizations`) to run all optimizer components attached to that avatar.
- **Top Editor Menu**:
  - `Run All Optimizations`: Batch runs optimization across all avatars with optimizer components in the active hierarchy.
  - `Avatar Optimizer Window`: Dashboard listing all scene avatars and presets with single-click execution and hierarchy focus buttons.

# Features

- **Automated Multi-Target Optimization**: Texture budget auto-tuning (VRAM & compressed asset bundle limits), PhysBone/Contact/Constraint pruning, mesh decimation, material deduplication, static mesh merging, NaNimation toggles, and non-animated blendshape baking.
- **Material Atlasing** *(opt-in)*: Packs compatible materials' textures into a shared atlas (growing binary-tree bin packing, per-map atlases sharing one layout, padded gutters) and rewrites mesh UV0 into the packed cells — the only route below a material slot limit that deduplication cannot reach. Gated hard: identical shader/queue/keywords, identical non-texture properties, UVs within `[0,1]`, and no vertices shared between submeshes; anything else is skipped with a logged reason.
- **Renderer Bounds & Probe Anchors**: Recalculates `localBounds` from mesh extent, bone reach and blendshape displacement after merging/atlasing, and gives all renderers a common probe anchor. Without this, merged renderers inherit bind-pose bounds and cull incorrectly.
- **Humanoid Rig Hygiene** *(opt-in)*: Jaw bone unmapping (so VRChat's visemes drive the mouth) and Legacy Blend Shape Normals. Both edit the shared model importer and therefore affect every avatar using that FBX.
- **Expression Parameter Budget**: Dead synced expression parameters are removed only when the avatar exceeds VRChat's synced parameter cap (or when the user explicitly opts in), stopping as soon as the avatar is back under it. Parameters referenced by any animator, parameter driver, PhysBone prefix, or contact receiver are never touched, and menu controls left dangling by a removal are pruned alongside them.
- **PhysBone Consolidation**: Sibling PhysBone chains with identical settings are collapsed into a single component rooted at their shared parent (excess subtrees excluded via `ignoreTransforms`), trading one extra affected transform for N-1 components. Runs *before* pruning so chains are consolidated rather than destroyed.
- **Shader & Material Conversion**: Automatic property remapping from Poiyomi, lilToon, Standard, etc., to mobile-compliant VRChat shaders (`VRChat/Mobile/*`).
- **Animation & Controller Rewriting**: Non-destructive cloning and rewriting of `AnimatorControllers`, `AnimatorOverrideControllers`, and `AnimationClip` material-swap curves to point at newly generated assets.

# Don'ts

- **Never Over Limits**: Never leave an optimized avatar in an un-uploadable state. Always bring metrics below target limits (distributing reductions evenly where possible).
- **Never Over-Optimize**: Always stay as close to limits as possible without exceeding them (e.g., a 20 MB asset bundle for a 200 MB cap is unacceptable quality loss).
- **No Temporary Avatars / Preserves Source**: Do not rely on temporary build-time avatars. Operations duplicate the target avatar directly in the scene as permanent platform clones (e.g. `Avatar_Android`), preserving the source avatar intact and disabled.
- **Non-Destructive Asset Management**: Never mutate original project files. Always create copied assets (materials, textures, meshes, controllers) in designated output folders (`Assets/_AVATAROPTIMIZER/<TargetAvatarName>/` or adjacent).

# Agent Notes

- Always clear the Unity Editor log (`ClearEditorLog`) before test runs to discern current run output from past executions.