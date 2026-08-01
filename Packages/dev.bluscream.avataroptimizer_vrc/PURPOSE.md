# Description

This Unity editor package (`dev.bluscream.avataroptimizer_vrc`) provides automated, platform-aware VRChat avatar optimization. It allows any avatar to be cut, decimated, pruned, and re-encoded to fit user-selected Platform + Performance Rank limits (read directly from the VRChat SDK or from `Packages/dev.bluscream.avataroptimizer_vrc/config.json`). 

The goal is to keep every metric under its target limit while preserving as much visual fidelity as possible — staying *close* to the limits rather than far under them.

It takes inspiration from the tools in `.references/` (d4rkAvatarOptimizer, Pumkin's Avatar Tools, material-combiner-addon, and the PxINKY optimization tutorial series) and offers optional, opt-in optimizations beyond what VRChat requires for upload.

Settings persist via `EditorPrefs`, written by the optimizer window.

The ultimate goal is a **one-click upload readiness** pipeline after selecting a preset. See *Known Gaps* — several passes have outstanding correctness issues, so this is a direction of travel, not a description of the current state.

## Third-Party Addon & Ecosystem Compatibility

- **VRCFury** — explicitly handled: components are scanned and their material references remapped onto the optimized copies.
- **Modular Avatar** — covered by the same generic pass rather than by MA-specific logic. `AvatarAnimationRewriter` walks every component's serialized properties for `Material` references and remaps them; MA components are only special-cased for log labelling. Anything MA does that is *not* a plain serialized material reference is not accounted for.
- **SPS / DPS / TPS** — `AvatarPenetratorDetector` recognises penetrator renderers by name and component heuristics and excludes them from mesh merging. It does not otherwise adapt them.
- **MMD blendshapes** — whitelisted by name in `AvatarBlendShapeOptimizer` so the blendshape stripper leaves them intact (but see the skinned-mesh-merge gap below, which discards them anyway).

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
- **Mesh & Material Consolidation**: `AvatarMaterialSlotOptimizer` deduplicates material slots losslessly — identical materials collapse and their submeshes merge, null slots drop with their triangles, and a renderer whose submesh count disagrees with its slot count is left untouched rather than mis-remapped. `AvatarMeshCountOptimizer` merges static MeshRenderers and (see Known Gaps) skinned meshes, skipping SPS/DPS penetrators and preferring not to merge the body/face renderer. Both only act when the avatar is over the profile limit.
- **Humanoid Rig Hygiene** *(opt-in)*: Jaw bone unmapping (so VRChat's visemes drive the mouth) and Legacy Blend Shape Normals. Both edit the shared model importer and therefore affect every avatar using that FBX.
- **Expression Parameter Budget**: Dead synced expression parameters are removed only when the avatar exceeds VRChat's synced parameter cap (or when the user explicitly opts in), stopping as soon as the avatar is back under it. Parameters referenced by any animator, parameter driver, PhysBone prefix, or contact receiver are never touched, and menu controls left dangling by a removal are pruned alongside them.
- **Dynamics Budgets**: `AvatarPhysBonePruner` enforces four budgets in order — component count, affected transforms, collider count, then collision checks — pruning deepest-first (accessory/detail bones before spine chains), or via an interactive checklist when `PhysBonePruningStrategy.InteractiveChecklist` is set. Colliders are dropped least-referenced-first, and collision checks are reduced by clearing collider *lists* rather than deleting more bones. `AvatarContactOptimizer` and `AvatarConstraintOptimizer` trim contacts and constraints to their caps; `AvatarLightOptimizer` disables (rather than deletes) excess lights, deepest-first. `AvatarPenetratorDetector` identifies DPS/TPS/SPS renderers by name and component heuristics so mesh merging leaves them alone.
- **PhysBone Consolidation**: Sibling PhysBone chains with identical settings are collapsed into a single component rooted at their shared parent (excess subtrees excluded via `ignoreTransforms`), trading one extra affected transform for N-1 components. Runs *before* pruning so chains are consolidated rather than destroyed.
- **Shader & Material Conversion**: Two cooperating layers. `ShaderMapping` picks the target `VRChat/Mobile/*` shader — config.json rules first (evaluated in `priority` order, matching by `Exact`/`StartsWith`/`EndsWith`/`Contains`/`Regex`, optionally gated on the material actually having given properties), then hardcoded heuristics across Toon Standard, Toon Lit, Standard Lite, Bumped Diffuse, Bumped Mapped Specular, Diffuse, Matcap Lit and the two Particles variants. Materials already on `VRChat/Mobile/*` are left alone. `ShaderPropertyMapper` then transfers values, remapping Poiyomi/lilToon/Standard property names onto the target's (`_NormalMap` → `_BumpMap`, `_PoiyomiEmissionMap` → `_EmissionMap`, …) from a universal table plus `Editor/Resources/ShaderPropertyMappings.json`.
- **Animation & Controller Rewriting**: Non-destructive cloning and rewriting of `AnimatorControllers`, `AnimatorOverrideControllers`, and `AnimationClip` material-swap curves to point at newly generated assets. Assets are only cloned when they actually reference something that changed — a controller with no remapped material keeps its original reference rather than spawning a redundant copy.
- **Blendshape Optimization**: Blendshapes not referenced by any animation curve are baked into base geometry (when their weight is non-zero) and stripped, with MMD morphs, VRC visemes and blinks whitelisted so `keepMMD` avatars stay MMD-compatible.

# Known Gaps

Findings from a file-by-file review, roughly most to least severe. Kept here so this document is not read as a promise. Nothing in the package has been compiled or run against a real avatar during that review — these are read-only findings.

## Correctness — passes that are on by default

- **Deleting unused GameObjects can break animation paths.** `AvatarComponentRemover.DeleteUnusedGameObjects` removes component-less, unreferenced transforms and re-parents their children to the grandparent. Animation curves address objects by hierarchy *path* string, so every re-parented child's curves silently stop resolving. `AvatarAnimationRewriter` runs at Step 4, well before this deletion at Step 7.5, so nothing fixes the paths afterwards. Either re-run a path fixup after deletion, or refuse to delete any transform that still has children.
- **The FX layer optimizer changes toggle behaviour.** `AvatarAnimatorOptimizer.OptimizeController` collapses two-state toggle layers into one Direct Blend Tree, but four things are wrong with the conversion:
  - **The off motion is discarded.** Only `onMotion` becomes a DBT child. Previously the off state actively animated properties to their off values; now nothing drives them at 0. On a Write Defaults Off avatar — the common VRChat setup — toggled-off properties simply keep whatever another layer last wrote.
  - **On and off are assumed to be `states[0]` and `states[1]`.** That order is serialization order, not semantics, so toggles can be silently inverted.
  - **Parameter defaults are dropped.** `EnsureParameterIsFloat` computes `defaultVal` from the Bool/Int parameter, then calls `RemoveParameter`/`AddParameter` without ever applying it — the local is dead code. A toggle that defaulted to on comes back defaulting to off.
  - **Layer masks, weights and blending modes are ignored.** A merged layer's avatar mask or Additive blend mode does not survive into the DBT.

  Until these are fixed, `OptimizeFXLayer` should arguably default to off.
- **Contact and constraint pruning has no priority order.** Unlike PhysBone pruning, which sorts deepest-first and offers an interactive checklist, `AvatarContactOptimizer` and `AvatarConstraintOptimizer` simply truncate the list in `GetComponentsInChildren` order. Which contacts survive is therefore an artifact of hierarchy layout — a head-pat receiver is as likely to be deleted as a decorative one. These should adopt the pruner's depth ordering, and ideally its checklist.
- **Skinned mesh merging destroys blendshapes.** `AvatarMeshCountOptimizer.CombineSkinnedMeshes` builds the merged mesh with `Mesh.CombineMeshes`, which does not carry blendshapes across. Every viseme, blink, MMD morph and toggle shape on the merged renderers is lost. This also silently undoes Step 6.5, which went to the trouble of whitelisting exactly those shapes. Merging skinned meshes on an avatar that uses blendshapes is currently unsafe.
- **Merged bone weights can be scrambled.** In the same method, `remappedBoneWeights` is accumulated in source-mesh order, but the final mesh is assembled after regrouping the combine instances *by material*, which reorders vertices. The guard is only `remappedBoneWeights.Count == combinedMesh.vertexCount` — a count check that usually passes while the weights no longer line up with their vertices, deforming the mesh. The weights need to be reordered alongside the regrouping, or gathered after the combine.
## Inherent limitations

- **Mobile shader conversion drops shader-specific features.** `VRChat/Mobile/*` has no equivalent for Poiyomi/lilToon hue shift, audio link, dissolve, or most animated shader properties, so any animation driving those properties survives the clip rewrite but no longer affects anything. `ShaderPropertyMapper` transfers what maps and reports the rest; it cannot preserve what the target shader does not implement. Emission *maps* do carry over, and are atlased alongside the other texture properties.
- **Atlasing skips more than the Blender workflow does.** Requiring identical non-texture properties means two materials differing only in `_Color` will not group — material-combiner handles that case by baking the diffuse colour into the atlas, which is not implemented here. Tiled UVs (common on clothing) are excluded outright. Expect fewer groups atlased than the tutorial's Blender pass achieves.

## Dead or stale

- **NaNimation toggles are not wired up.** `AvatarNaNimationOptimizer` exists and provides `GetOrCreateNaNToggleBone` / `InjectNaNAnimationCurves`, but nothing in the pipeline calls it. The `UseNaNimationToggles` config flag is exposed in the window and persisted to `EditorPrefs`, yet is read by no code path. Either wire it into Step 7.5 (it needs to run alongside skinned mesh merging to be useful) or remove the flag.
- **`README.md` contradicts this document.** Its `ConversionConfig` reference still lists `MaxTextureResolution`, `CrunchCompressionQuality`, `UncompressedAvatarHeadroomMB`, `CompressedAvatarHeadroomMB` and `CrunchStepPercent`, none of which exist any more — texture tuning moved to the `TextureAutoTuning` constants and the Step 8.5 convergence loop. It omits every flag added since (`OptimizeFXLayer`, `BakeNonAnimatedBlendshapes`, `KeepMMDBlendshapes`, `DeleteUnusedGameObjects`, `MergeSiblingPhysBones`, `CleanExpressionParametersWhenOverBudget`, `ForceCleanExpressionParameters`, `FixRendererBounds`, `AnchorProbesToHips`, `AtlasMaterials`, `UnmapJawBone`, `EnableLegacyBlendShapeNormals`, `SkipDryRunBundleBuild`, `MaxSizeConvergenceAttempts`) and every module added since (`AvatarPhysBoneMerger`, `AvatarTextureAtlaser`, `TextureAtlasPacker`, `AvatarBoundsOptimizer`, `AvatarRigOptimizer`, `AvatarExpressionParameterCleaner`, `AvatarAnimatorOptimizer`, `AvatarBlendShapeOptimizer`, `AvatarNaNimationOptimizer`, `AvatarLightOptimizer`, `AvatarPenetratorDetector`, `AvatarBudgetReducers`). It also calls the window a "dashboard", which it is not. `package.json` is still at `1.3.1` despite all of the above.
- **`ConversionConfig.AvatarSuffix` is not exposed in the UI.** Every other config field is; this one can only be set from code.

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