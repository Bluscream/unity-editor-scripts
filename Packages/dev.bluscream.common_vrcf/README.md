# Bluscream Common VRCFury (`dev.bluscream.common_vrcf`)

Reflection helpers, expression menu mapping, tree generation, and feature management for VRCFury.

## Overview

`dev.bluscream.common_vrcf` provides a dedicated abstraction layer for interacting with VRCFury. It includes reflection helpers to access internal VRCFury types (`VFGameObject`, `MenuEstimator`, `MenuManager`, `VRCFury`), full expression menu hierarchy mapping, and programmatic creation of menu move features.

---

## API Reference

### 1. `VRCFuryHelper` (`VRCFuryHelper.cs`)

Handles VRCFury type reflection, assembly discovery, and component lookups.

#### Properties
- `VFGameObjectType`: `Type` - Reflected `VF.Utils.VFGameObject` type.
- `MenuEstimatorType`: `Type` - Reflected `VF.Utils.MenuEstimator` type.
- `MenuManagerType`: `Type` - Reflected `VF.Utils.MenuManager` type.
- `VRCFuryComponentType`: `Type` - Reflected `VF.Model.VRCFury` type.
- `IsInitialized`: `bool` - `true` if VRCFury assemblies and reflection methods are initialized.

#### Methods
- `Initialize()`: `bool`
  - Discovers VRCFury assemblies and caches reflection MethodInfos.
- `IsVRCFuryInstalled()`: `bool`
  - Checks if VRCFury is installed and accessible in the Unity project.
- `GetVRCFuryComponents(GameObject avatarRoot)`: `List<Component>`
  - Retrieves all active VRCFury components attached to the avatar hierarchy.

---

### 2. `VRCFuryMenuMapper` (`VRCFuryMenuMapper.cs`)

Generates merged expression menu trees and paths using VRCFury menu estimation algorithms.

#### Data Structures

##### `MenuItemNode`
- `Name`: `string` - Control/folder name.
- `FullPath`: `string` - Complete menu path (e.g. `Clothing/Jacket/Toggle`).
- `Control`: `VRCExpressionsMenu.Control` - Associated VRChat Expressions Menu control asset.
- `ParentMenu`: `VRCExpressionsMenu` - Parent menu container asset.
- `Children`: `List<MenuItemNode>` - Sub-nodes and sub-menus.

##### `MenuMoveOperation`
- `FromPath`: `string` - Source menu path.
- `ToPath`: `string` - Destination menu path.

#### Methods
- `GetMergedMenu(GameObject avatarObj)`: `VRCExpressionsMenu`
  - Evaluates avatar VRCFury components and returns full merged VRChat expressions menu asset.
- `BuildMenuTree(GameObject avatarObj)`: `MenuItemNode`
  - Constructs a complete hierarchical tree of all merged menu controls and sub-menus.
- `GetAllMenuPaths(GameObject avatarObj)`: `List<string>`
  - Flattens the merged expression menu hierarchy into a list of menu item path strings.

---

### 3. `VRCFuryFeatureHelper` (`VRCFuryFeatureHelper.cs`)

Programmatic management of VRCFury components and features.

#### Methods
- `ApplyMenuMoves(GameObject avatarObject, List<MenuMoveOperation> moves, string containerName = "[VRCFury] Menu Moves")`: `void`
  - Programmatically attaches VRCFury components with `MoveMenuItem` features to move menu items on build.
- `ClearMenuMoves(GameObject avatarObject, string containerName = "[VRCFury] Menu Moves")`: `void`
  - Destroys the menu moves container GameObject attached to the avatar.
