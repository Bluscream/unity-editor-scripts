# unity-editor-scripts

Useful scripts for the Unity Editor, packaged as a **VRChat Creator Companion (VCC) / VPM listing**.

## 📦 Add to VCC

**Listing URL:** `https://bluscream.github.io/unity-editor-scripts/index.json`

- **One click:** open the [listing page](https://bluscream.github.io/unity-editor-scripts/) and press **Add to VCC**, or use this direct link:
  [`vcc://vpm/addRepo?url=https://bluscream.github.io/unity-editor-scripts/index.json`](vcc://vpm/addRepo?url=https://bluscream.github.io/unity-editor-scripts/index.json)
- **Manual:** in VCC go to *Settings → Packages → Add Repository* and paste the listing URL above.

Once the repository is added, the packages below appear in VCC and can be added to any project.

## Packages

| Package | Description |
| --- | --- |
| `dev.bluscream.common` | Common utilities shared by the other packages. |
| `dev.bluscream.backupsystem` | Backup & restore for assets, GameObjects, and components. |
| `dev.bluscream.cleanup` | Smart cleanup of unused assets. |
| `dev.bluscream.componentremover` | Find & remove missing scripts from GameObjects. |
| `dev.bluscream.menumanager` | Mass re-organize & visually edit VRChat avatar menus (VRCFury). |
| `dev.bluscream.questpatcher` | Convert VRChat avatars for Quest/Android compatibility. |
| `dev.bluscream.replacer` | Replace GameObjects with other objects or prefabs. |
| `dev.bluscream.shadertest` | Quick shader preview / testing tool. |
| `dev.bluscream.texturecompressor` | Apply texture compression settings in bulk. |

## How the listing is built

`.github/workflows/build-listing.yml` runs on every push to `main`:

1. Zips each `Packages/*/` folder.
2. Publishes each zip as a GitHub **Release** (`<name>-<version>`).
3. Generates a VPM `index.json` (via `.github/scripts/build-index.mjs`) referencing those release zips with their SHA-256.
4. Publishes `index.json` + a landing page to the **`gh-pages`** branch, which **GitHub Pages** serves.

To release a new version, bump the `version` in that package's `package.json` and push.

> **Pages setup:** *Settings → Pages* is configured to deploy from the **`gh-pages` branch** (root). The
> initial release was built and published manually; the workflow above takes over automatically once
> GitHub Actions is available on the account.

## Note on `.meta` files

This repo's `.gitignore` currently excludes Unity `*.meta` files. VPM/UPM packages should ship their `.meta` files so asset GUIDs (and asmdef references) stay stable across installs. If you hit missing-reference issues after installing, commit the packages' `.meta` files (remove the `*.meta` rule for `Packages/`).
