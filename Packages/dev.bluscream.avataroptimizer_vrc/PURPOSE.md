# Description

This Unity editor package (`dev.bluscream.avataroptimizer_vrc`) provides automated, platform-aware VRChat avatar optimization. It allows any avatar to be cut, decimated, pruned, and re-encoded to fit user-selected Platform + Performance Rank limits (read directly from the VRChat SDK or from `Packages/dev.bluscream.avataroptimizer_vrc/config.json`). 

The goal is to keep every metric under its target limit while preserving as much visual fidelity as possible — staying *close* to the limits rather than far under them.

It takes inspiration from the tools in `.references/` (d4rkAvatarOptimizer, Pumkin's Avatar Tools, material-combiner-addon, and the PxINKY optimization tutorial series) and offers optional, opt-in optimizations beyond what VRChat requires for upload.

Settings persist via `EditorPrefs`, written by the optimizer window.

The ultimate goal is a **one-click upload readiness** pipeline after selecting a preset. See *Known Gaps* for what is still missing.

## Third-Party Addon & Ecosystem Compatibility

- **VRCFury** — explicitly handled: components are scanned and their material references remapped onto the optimized copies.
- **Modular Avatar** — covered by the same generic pass rather than by MA-specific logic. `AvatarAnimationRewriter` walks every component's serialized properties for `Material` references and remaps them; MA components are only special-cased for log labelling. Anything MA does that is *not* a plain serialized material reference is not accounted for.
- **SPS / DPS / TPS** — `AvatarPenetratorDetector` recognises penetrator renderers by name and component heuristics and excludes them from mesh merging. It does not otherwise adapt them.
- **MMD blendshapes** — whitelisted by name in `AvatarBlendShapeOptimizer` so the blendshape stripper leaves them intact; skinned mesh merging rebuilds them on the combined mesh, merging same-named shapes across sources into one.

# UI / UX

## Implemented

- **`Bluscream/VRChat/Avatar Optimizer`** — the single menu entry, opening `VRCAvatarOptimizerWindow`. One avatar at a time via an "Avatar Root" object field, with platform/rank selection, every pass toggle, and a run button. All settings persist to `EditorPrefs`.
- **PhysBone prune checklist** — when `PhysBonePruningStrategy.InteractiveChecklist` is selected, `PhysBonePruneChecklistWindow` opens with the deepest-first suggestion pre-ticked and per-PhysBone transform counts, so the automatic choice can be overridden. Cancelling falls back to the automatic selection.
- **`ConversionSummary`** — collects successes, warnings and errors (with object references for click-to-select) plus before/after stats, and reports them after a run.

## Planned (not implemented)

Nothing below exists in code yet; the package currently has exactly one `MenuItem` and no custom components or editors.

- **Avatar preset components** — an `Avatar Optimizer` MonoBehaviour on avatar roots holding preset settings, with an "Advanced Settings" foldout for manual limit overrides and its own "Run Optimizations" button. There is no such component and no `CustomEditor`.
- **Hierarchy context menu** — a right-click `Run Optimizations` entry on avatar roots.
- **`Run All Optimizations`** — batch execution across every avatar in the scene.
- **Multi-avatar dashboard** — the window takes a single avatar via an object field; it does not enumerate scene avatars or offer hierarchy focus buttons.
- **A dedicated settings window** — defaults live in `EditorPrefs`, written by the main window; there is no separate settings UI.

# Configuration

Limits resolve in layers, each overriding the last:

1. **Hardcoded C# defaults** in `Editor/PlatformProfiles/<Platform>/<Rank>.cs`.
2. **`config.json`** (`Packages/dev.bluscream.avataroptimizer_vrc/config.json`), validated against `config.schema.json`. Platform-level `limits` apply to every rank; per-rank `limits` override them.
3. **A remote `config.json`** fetched from GitHub — see the warning below.
4. **The VRChat SDK**, applied last and authoritative for anything it reports.

Merge semantics are sentinel-guarded: `int.MaxValue` / `long.MaxValue` mean "unspecified", so an omitted field inherits rather than resetting to unlimited. `ApplyFrom` merges in place; `MergeWith` returns a new instance. Two fields have no sentinel and follow special rules — component blacklists union while a whitelist replaces, and the `Particle*Allowed` bools are restrictive-wins (an overlay may forbid, never re-allow, since C#'s default `true` is indistinguishable from "unset").

> **Remote config fetch.** `OptimizerConfig` is `[InitializeOnLoad]` and, on every editor load, fires a background `Task` that downloads `config.json` from a hardcoded GitHub raw URL (5s timeout). If it parses and validates, it silently replaces the active config — meaning shader mappings and every platform limit can change without a package update, and the editor makes an outbound network request on load. Two things to be aware of: this is a supply-chain surface (whoever controls that URL controls the limits and shader remapping), and `ActiveConfig` is reassigned from a background thread while the main thread may be reading it, with no synchronization. Failures are non-fatal and fall back to the local file.

# Platform Profiles

`Editor/PlatformProfiles/` holds one class per Platform × Rank (PC, Android, iOS × Excellent, Good, Medium, Poor, VeryPoor). `VeryPoor` sets most limits to `int.MaxValue` — it is the "no rank target" case, capped only by texture memory and the bundle size.

- **iOS inherits from Android** (`PlatformProfile_iOS : PlatformProfile_Android`), matching VRChat's own fallback of serving the Android build when no iOS upload exists. Limits, component blacklists, conversions and validation are all shared.
- **Bundle size caps come from the SDK at runtime** via `GetSdkAssetBundleSizeLimit`, falling back to 200 MB (PC) / 10 MB (mobile), so SDK changes are picked up without a package update.
- **Two different kinds of limit.** Rank limits (e.g. Good on Quest allows 1 material slot) are *performance* targets that the optimizer drives toward. Separately, `ValidatePlatformRules` warns on VRChat's *hard* mobile cap of 4 material slots per renderer, which blocks upload regardless of the rank being targeted. A profile can therefore be within its hard limits while still missing its rank target.
- **Component removal is config-driven** (`config.json` → `ComponentBlacklist`), with `ShouldRemoveComponentCustom` handling type-based checks that cannot be expressed as strings — cameras, joints, DynamicBones, FinalIK/RootMotion, post-processing, and non-VRC Unity constraints on mobile.
- **Android/iOS conversions** clamp `QualitySettings.pixelLightCount` to 1, which VRChat's mobile build otherwise rejects. Note this mutates a *project* setting, not the avatar.

# Pipeline

`VRCAvatarOptimizerCore.ConvertAvatar` runs these steps in order. Everything happens inside a single Undo group (asset writes excepted).

| Step | Pass | Default |
|---|---|---|
| 0 | Switch active build target to the target platform | always |
| 1 | Duplicate avatar, apply platform suffix, disable source | `DuplicateAvatar` |
| 1.5 | Humanoid rig hygiene (jaw unmap, legacy blendshape normals) | **opt-in** |
| 2 | Remove platform-incompatible components | **opt-in** (SDK Auto Fix converts rather than deletes) |
| 3 | Duplicate materials, remap shaders to mobile equivalents | `ReplaceShaders` |
| 4 | Rewrite AnimatorControllers, clips, material swaps, VRCFury | `RemapAnimationsAndVRCFury` |
| 4.5 | FX layer optimization (direct blend tree collapsing) | `OptimizeFXLayer` |
| 5 | Texture budget allocation (VRAM + estimated bundle share) | `OptimizeTextures` |
| 5.5 | PhysBone sibling consolidation | `MergeSiblingPhysBones` |
| 6 | PhysBone pruning to rank limits | `PruningStrategy` |
| 6.5 | Non-animated blendshape baking and stripping | `BakeNonAnimatedBlendshapes` |
| 7 | Mesh decimation to the triangle budget | `DecimateMeshes` |
| 7.5 | Material slot dedup → atlasing → mesh count → lights → unused GameObjects → expression parameters → bounds/anchors | mixed |
| 8 | Platform-specific conversions and rule validation | always |
| 8.5 | Iterative AssetBundle size convergence | `SkipDryRunBundleBuild` inverts |

Step 8.5 measures a real SDK dry-run build and feeds it to a reducer ladder — textures first (cheap), mesh decimation only once textures are exhausted (destructive to silhouettes and blendshapes). The texture disk model self-calibrates from consecutive (estimate, measured) samples, so systematic error corrects itself rather than accumulating. Incompatible components are always stripped temporarily around the dry-run builds and restored afterwards, so the measured size matches an SDK-auto-fixed upload.

# Features

- **Automated Multi-Target Optimization**: Texture budget auto-tuning (VRAM & compressed asset bundle limits), PhysBone pruning, mesh decimation, material deduplication, static mesh merging, and non-animated blendshape baking.
- **Material Atlasing** *(opt-in)*: Packs compatible materials' textures into a shared atlas (growing binary-tree bin packing, per-map atlases sharing one layout, padded gutters) and rewrites mesh UV0 into the packed cells — the only route below a material slot limit that deduplication cannot reach. Gated hard: identical shader/queue/keywords, identical non-texture properties, UVs within `[0,1]`, and no vertices shared between submeshes; anything else is skipped with a logged reason.
- **Renderer Bounds & Probe Anchors**: Recalculates `localBounds` from mesh extent, bone reach and blendshape displacement after merging/atlasing, and gives all renderers a common probe anchor (the humanoid Hips by default). Without this, merged renderers inherit bind-pose bounds and cull incorrectly. Also clears `updateWhenOffscreen`, which is pure per-frame CPU cost once bounds are correct.
- **Mesh & Material Consolidation**: `AvatarMaterialSlotOptimizer` deduplicates material slots losslessly — identical materials collapse and their submeshes merge, null slots drop with their triangles, and a renderer whose submesh count disagrees with its slot count is left untouched rather than mis-remapped. `AvatarMeshCountOptimizer` merges static MeshRenderers and skinned meshes, skipping SPS/DPS penetrators and preferring not to merge the body/face renderer. The skinned merge assembles vertex buffers by hand rather than via `Mesh.CombineMeshes`, so vertex order is explicit and bone weights and blendshapes stay aligned; blendshapes are rebuilt on the combined mesh with same-named shapes merging across sources. Both only act when the avatar is over the profile limit.\n- **NaNimation Toggles**: A mesh animated on/off normally cannot be merged, since merging destroys the GameObject the toggle animates. Each such mesh instead gets a dedicated toggle bone in its free fourth bone slot at weight 0, and its `m_IsActive` curves are rewritten to drive that bone's scale to `NaN`. Skinning multiplies by weight and `0 * NaN = NaN`, so the mesh's vertices go NaN and the GPU discards its triangles, while a scale of 1 leaves rendering untouched. Meshes whose vertices already use four bones are left unmerged rather than losing skinning influence.
- **Humanoid Rig Hygiene** *(opt-in)*: Jaw bone unmapping (so VRChat's visemes drive the mouth) and Legacy Blend Shape Normals. Both edit the shared model importer and therefore affect every avatar using that FBX.
- **Expression Parameter Budget**: Dead synced expression parameters are removed only when the avatar exceeds VRChat's synced parameter cap (or when the user explicitly opts in), stopping as soon as the avatar is back under it. Parameters referenced by any animator, parameter driver, PhysBone prefix, or contact receiver are never touched, and menu controls left dangling by a removal are pruned alongside them.
- **Dynamics Budgets**: `AvatarPhysBonePruner` enforces four budgets in order — component count, affected transforms, collider count, then collision checks — pruning deepest-first (accessory/detail bones before spine chains), or via an interactive checklist when `PhysBonePruningStrategy.InteractiveChecklist` is set. Colliders are dropped least-referenced-first, and collision checks are reduced by clearing collider *lists* rather than deleting more bones. `AvatarContactOptimizer` and `AvatarConstraintOptimizer` trim contacts and constraints to their caps; `AvatarLightOptimizer` disables (rather than deletes) excess lights, deepest-first. `AvatarPenetratorDetector` identifies DPS/TPS/SPS renderers by name and component heuristics so mesh merging leaves them alone.
- **PhysBone Consolidation**: Sibling PhysBone chains with identical settings are collapsed into a single component rooted at their shared parent (excess subtrees excluded via `ignoreTransforms`), trading one extra affected transform for N-1 components. Runs *before* pruning so chains are consolidated rather than destroyed.
- **Shader & Material Conversion**: Two cooperating layers. `ShaderMapping` picks the target `VRChat/Mobile/*` shader — config.json rules first (evaluated in `priority` order, matching by `Exact`/`StartsWith`/`EndsWith`/`Contains`/`Regex`, optionally gated on the material actually having given properties), then hardcoded heuristics across Toon Standard, Toon Lit, Standard Lite, Bumped Diffuse, Bumped Mapped Specular, Diffuse, Matcap Lit and the two Particles variants. Materials already on `VRChat/Mobile/*` are left alone. `ShaderPropertyMapper` then transfers values, remapping Poiyomi/lilToon/Standard property names onto the target's (`_NormalMap` → `_BumpMap`, `_PoiyomiEmissionMap` → `_EmissionMap`, …) from a universal table plus `Editor/Resources/ShaderPropertyMappings.json`.
- **Animation & Controller Rewriting**: Non-destructive cloning and rewriting of `AnimatorControllers`, `AnimatorOverrideControllers`, and `AnimationClip` material-swap curves to point at newly generated assets. Assets are only cloned when they actually reference something that changed — a controller with no remapped material keeps its original reference rather than spawning a redundant copy.
- **Blendshape Optimization**: Blendshapes not referenced by any animation curve are baked into base geometry (when their weight is non-zero) and stripped, with MMD morphs, VRC visemes and blinks whitelisted so `keepMMD` avatars stay MMD-compatible.

# Known Gaps

Remaining limitations after the file-by-file review. The correctness defects that review found have been fixed; what follows is what is still true. Nothing here has been compiled or run — there is no Unity or VRChat SDK in the development environment.

## Inherent limitations

- **Mobile shader conversion drops shader-specific features.** `VRChat/Mobile/*` has no equivalent for Poiyomi/lilToon hue shift, audio link, dissolve, or most animated shader properties, so any animation driving those properties survives the clip rewrite but no longer affects anything. `ShaderPropertyMapper` transfers what maps and reports the rest; it cannot preserve what the target shader does not implement. Emission *maps* do carry over, and are atlased alongside the other texture properties.
- **Atlasing skips more than the Blender workflow does.** Requiring identical non-texture properties means two materials differing only in `_Color` will not group — material-combiner handles that case by baking the diffuse colour into the atlas, which is not implemented here. Tiled UVs (common on clothing) are excluded outright. Expect fewer groups atlased than the tutorial's Blender pass achieves.
- **NaNimation needs a free fourth bone slot.** The toggle bone is added at weight 0, which works because skinning multiplies by weight and `0 * NaN = NaN`. A mesh whose vertices already use four bone influences would have to give one up, changing deformation, so those meshes are left unmerged instead. d4rkAvatarOptimizer offers an explicit "Allow 3 Bone Skinning" option to trade that away; this package does not.
- **Merging raises bone count.** Bones are only shared between source meshes when the bindpose matches as well as the transform, since the same transform with a different bindpose is a different space. This is correct but can push `MaxBones` up, and no pass reduces bone count.

## Not implemented

- **`ConversionConfig.AvatarSuffix` is not exposed in the UI.** Every other config field is; this one can only be set from code.
- The UI items listed under *UI / UX → Planned*.

# File Map

| Area | Files |
|---|---|
| Pipeline driver | `VRCAvatarOptimizerCore.cs` |
| Config | `OptimizerConfig.cs`, `config.json`, `config.schema.json` |
| Profiles | `PlatformProfiles/PlatformProfile.cs`, `PC.cs`, `Android.cs`, `iOS.cs`, and `{PC,Android,iOS}/{Excellent,Good,Medium,Poor,VeryPoor}.cs` |
| Shaders | `ShaderMapping.cs`, `ShaderPropertyMapper.cs`, `Resources/ShaderPropertyMappings.json` |
| Mesh & texture | `Optimizers/AvatarMeshCountOptimizer.cs`, `AvatarMaterialSlotOptimizer.cs`, `AvatarBlendShapeOptimizer.cs`, `AvatarTextureAtlaser.cs`, `TextureAtlasPacker.cs`, `AvatarBoundsOptimizer.cs` |
| Dynamics | `Optimizers/AvatarPhysBonePruner.cs`, `AvatarPhysBoneMerger.cs`, `AvatarContactOptimizer.cs`, `AvatarConstraintOptimizer.cs`, `AvatarParticleOptimizer.cs`, `AvatarLightOptimizer.cs`, `AvatarPenetratorDetector.cs` |
| Animation & params | `Optimizers/AvatarAnimationRewriter.cs`, `AvatarAnimatorOptimizer.cs`, `AvatarNaNimationOptimizer.cs`, `AvatarExpressionParameterCleaner.cs` |
| Removal & budgets | `Optimizers/AvatarComponentRemover.cs`, `AvatarBudgetReducers.cs` |
| Rig | `Optimizers/AvatarRigOptimizer.cs` |
| UI & reporting | `VRCAvatarOptimizerWindow.cs`, `PhysBonePruneChecklistWindow.cs`, `ConversionSummary.cs` |
| Packaging | `package.json`, `README.md`, `Editor/dev.bluscream.avataroptimizer_vrc.Editor.asmdef` |

# Don'ts

- **Never Over Limits**: Never leave an optimized avatar in an un-uploadable state. Always bring metrics below target limits (distributing reductions evenly where possible).
- **Never Over-Optimize**: Always stay as close to limits as possible without exceeding them (e.g., a 20 MB asset bundle for a 200 MB cap is unacceptable quality loss).
- **No Temporary Avatars / Preserves Source**: Do not rely on temporary build-time avatars. Operations duplicate the target avatar directly in the scene as permanent platform clones (e.g. `Avatar_Android`), preserving the source avatar intact and disabled.
- **Non-Destructive Asset Management**: Never mutate original project files. Always create copied assets (materials, textures, meshes, controllers) in designated output folders (`Assets/_AVATAROPTIMIZER/<TargetAvatarName>/` or adjacent).

# Agent Notes

- Always clear the Unity Editor log (`ClearEditorLog`) before test runs to discern current run output from past executions.