# Bluscream Common (`dev.bluscream.common`)

Core utilities, reflection helpers, asset backup/restore systems, transform extensions, and shared editor functions for Unity projects.

## Overview

`dev.bluscream.common` provides a comprehensive foundation for backup management (GameObjects, Materials, Textures, Assets, and Components), reflection utilities, extension methods, and UI helpers across all Bluscream Unity packages.

---

## API Reference

### 1. `Bluscream` Extension & Utility Suite (`Bluscream.cs`)

#### Constants & Fields
- `COMMON_VERSION`: `string` ("1.0.1") - Version identifier.

#### Extension Methods
- `CountDescendants(this Transform transform)`: `int`
  - Counts all descendant transform objects recursively.
- `GetPath(this Transform transform)`: `string`
  - Returns hierarchy path relative to scene root (e.g. `Armature/Hips/Spine`).
- `GetPathRelativeTo(this Transform transform, Transform root)`: `string`
  - Returns path relative to specified ancestor root transform.

#### Reflection & Type Helpers
- `GetPropertyOrFieldValue(object obj, string name)`: `object`
  - Dynamically extracts field or property value by reflection.
- `SetPropertyOrFieldValue(object obj, string name, object value)`: `bool`
  - Dynamically sets field or property value by reflection.

---

### 2. Backup System (`BackupSystem.cs` & Backup Modules)

Provides full backup, restore, serialization, and diff capabilities for GameObjects, Materials, Textures, and Assets.

#### Core Backup Classes

##### `BackupSystem`
- `CreateBackup(GameObject targetObj, string note = "")`: `BackupData`
  - Creates a full hierarchy backup of the specified GameObject.
- `RestoreBackup(BackupData backup, GameObject targetObj = null)`: `bool`
  - Restores GameObject structure, components, and references from backup data.
- `GetBackupsForObject(GameObject obj)`: `List<BackupData>`
  - Retrieves all active backups associated with the given GameObject.

##### `MaterialBackup`
- `CreateBackup(Material mat)`: `MaterialBackupData`
  - Captures material shader properties, textures, colors, floats, and keywords.
- `RestoreBackup(MaterialBackupData backup, Material targetMat)`: `bool`
  - Restores all property settings to target material.

##### `TextureBackup`
- `CreateBackup(Texture2D tex)`: `TextureBackupData`
  - Preserves texture importer settings, format, compression, and max resolution.
- `RestoreBackup(TextureBackupData backup, Texture2D tex)`: `bool`
  - Re-applies preserved importer configuration and reimports asset.

##### `AssetBackup`
- `CreateAssetBackup(UnityEngine.Object asset, string destinationFolder)`: `string`
  - Duplicates and stores an asset file copy with timestamped metadata.

---

### 3. Data Schemas & Models (`BackupData.cs`, `BackupMetadata.cs`, `BackupConfig.cs`)

#### Enums
- `BackupScope`: `FullHierarchy`, `SingleObject`, `MaterialsOnly`, `ComponentsOnly`
- `BackupStorageType`: `InMemory`, `ProjectFolder`, `UserAppData`

#### Classes & Structures
- `BackupConfig`: Holds global backup settings (max backups per object, auto-backup on build, compress backups).
- `BackupMetadata`: Stores timestamp, author, target object GUID, scene path, and notes.

---

## Editor Windows & Menus

- `BackupWindow` (`Tools/Bluscream/Backup Manager`): Visual Editor Window for listing, creating, comparing diffs, and restoring object/material/texture backups.
- `GameObjectBackupMenu` (`GameObject/Bluscream/Backup Object`): Hierarchy context menu shortcuts.
