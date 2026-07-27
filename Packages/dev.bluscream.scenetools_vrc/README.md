# VRChat Scene Tools (`dev.bluscream.scenetools_vrc`)

Collection of Unity Editor scene utilities, asset cleanup tools, texture compression ladder editors, shader usage finders, and component removers.

## Overview

`dev.bluscream.scenetools_vrc` provides workflow tools for searching and auditing texture memory, replacing scene GameObjects, batch re-compressing texture formats, finding shader usages, and performing deep asset/component cleanup across Unity scenes and avatar roots.

---

## API Reference & Editor Tools

### 1. `TextureCompressionEditor` (`Editor/TextureCompressionEditor.cs`)

High-performance texture importer auditor and batch re-compressor.

#### Methods
- `GetUniqueTextureImporters(GameObject root)`: `List<TextureImporter>`
  - Collects all unique texture importers referenced across materials on the target GameObject.
- `ApplyTextureSettings(List<TextureImporter> importers, int maxResolution, TextureImporterFormat format, int compressionQuality = 50, Action<string> progressCallback = null)`: `void`
  - Re-configures texture importer max resolutions, formats, and compression quality settings, and re-imports assets.

---

### 2. `AssetCleanup` & `ComponentRemover` (`Editor/AssetCleanup.cs`, `Editor/ComponentRemover.cs`)

Audits unreferenced assets, duplicate materials, and strips specified component types.

#### Methods
- `FindUnusedAssets()`: `List<string>`
  - Scans project folders for assets not referenced in active scenes or prefabs.
- `RemoveComponentsByTypeName(GameObject root, string[] typeNames)`: `int`
  - Removes components whose class or type names match any item in `typeNames`.

---

### 3. Editor Windows & Menus

- `TextureUsageWindow` (`Tools/Bluscream/Scene/Texture Memory Auditor`): Inspects VRAM usage per texture and renderer in scene.
- `FindShaderUsageWindow` (`Tools/Bluscream/Scene/Shader Usage Finder`): Lists all materials and objects using a specific shader.
- `GameObjectReplacer` (`Tools/Bluscream/Scene/GameObject Replacer`): Batch-replaces scene GameObjects while preserving transform hierarchy positions.
- `CleanupWindow` (`Tools/Bluscream/Scene/Asset & Component Cleanup`): Deep cleaning UI for unused materials, components, and missing scripts.
