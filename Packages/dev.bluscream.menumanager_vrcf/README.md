# VRCFury Menu Manager (`dev.bluscream.menumanager_vrcf`)

A visual Unity Editor window for managing, reorganizing, and mass-moving VRChat expression menus via VRCFury.

## Overview

`dev.bluscream.menumanager_vrcf` leverages `dev.bluscream.common_vrcf` to inspect merged avatar expression menu trees, visualize sub-menus and controls, and generate VRCFury `MoveMenuItem` features to reorganize avatar menus cleanly without destroying original assets.

---

## API Reference & Main Classes

### 1. `MenuManagerWindow` (`Editor/MenuManagerWindow.cs`)
EditorWindow UI dashboard for inspecting avatar merged menus, viewing hierarchy trees, filtering controls, and initiating move operations.

#### Key Functions
- `ShowWindow()`: Opens the Menu Manager window (`Tools/Bluscream/VRChat/VRCFury Menu Manager`).
- `LoadAvatarMenu(GameObject avatarObj)`: Loads merged menu hierarchy for selected avatar.
- `ApplyChanges()`: Generates VRCFury `MoveMenuItem` components on avatar container object.

---

### 2. `MassMoveWindow` (`Editor/MassMoveWindow.cs`)
Modal sub-window for batch-moving multiple menu controls matching path patterns or folder structures.

---

### 3. Data Models (`Editor/MenuModels.cs`)
- `MenuNodeData`: Data model representing menu folders, items, control types, and paths.
- `MoveItemData`: Data structure holding source and target paths for queued move operations.
