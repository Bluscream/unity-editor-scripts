# VRC-QuestPatcher

Automated tool to convert VRChat avatars for Quest/Android compatibility.

## Features

- **Component Removal**: Automatically removes Quest-incompatible components (DynamicBones, Cloth, Cameras, Lights, AudioSources, Physics components, etc.)
- **Shader Replacement**: Replaces PC shaders with Quest-compatible VRChat/Mobile shaders using intelligent matching
- **Texture Optimization**: Optional texture compression to save VRAM
- **Backup & Restore**: Creates backups before conversion and allows restoration
- **Progress Monitoring**: Real-time progress display during conversion
- **Detailed Summary**: Shows what was changed with clickable object references

## Usage

1. Open the window: `Tools > VRC-QuestPatcher`
2. Drag your avatar root GameObject (with VRC_AvatarDescriptor) into the drop area
3. Configure options:
   - Enable/disable component removal
   - Enable/disable shader replacement
   - Enable/disable texture optimization
   - Set compression quality and texture size limits
4. Click "Start Conversion"
5. Review the summary and click on any errors/warnings to jump to the affected objects

## Shader Replacement

The tool uses a comprehensive lookup table and automatic pattern matching to replace shaders:

- **Poiyomi shaders** → `VRChat/Mobile/Toon Standard` or `Toon Lit`
- **Unity Standard** → `VRChat/Mobile/Standard Lite`
- **Unity Standard (Specular)** → `VRChat/Mobile/Bumped Mapped Specular`
- **Unity Diffuse** → `VRChat/Mobile/Diffuse`
- And many more patterns...

If no exact match is found, the tool attempts automatic keyword-based matching.

## Quest-Compatible Shaders

The following shaders are Quest-compatible and will be used for replacements:

- `VRChat/Mobile/Toon Standard`
- `VRChat/Mobile/Toon Lit`
- `VRChat/Mobile/Standard Lite`
- `VRChat/Mobile/Bumped Diffuse`
- `VRChat/Mobile/Bumped Mapped Specular`
- `VRChat/Mobile/Diffuse`
- `VRChat/Mobile/Matcap Lit`
- `VRChat/Mobile/Particles/Additive`
- `VRChat/Mobile/Particles/Multiply`

## Removed Components

The following components are removed for Quest compatibility:

- DynamicBone (use PhysBones instead)
- Cloth
- Camera (on avatars)
- Light (on avatars)
- AudioSource (on avatars)
- Rigidbody, Collider, Joint (on avatars)
- ParticleSystem (with limits)
- Unity Constraints
- FinalIK
- Post-processing components

## Backup

Backups are automatically created before conversion and include:

- Material shader paths and properties
- Component inventory
- Texture import settings

Backups are stored in the configured location (default: `Assets/VRCQuestPatcherBackups/`)

## Requirements

- Unity 2019.4 or later
- VRChat SDK (for VRC_AvatarDescriptor component)

## Compatibility

The package is compatible with Unity versions 2019.4 through 2023.3+ with appropriate version-specific code paths.
