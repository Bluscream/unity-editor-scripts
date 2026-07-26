using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public enum TargetPlatform
    {
        PC,
        Android,
        iOS
    }

    public enum AvatarPerformanceRank
    {
        Excellent,
        Good,
        Medium,
        Poor,
        VeryPoor
    }

    public enum AssetPlacementLocation
    {
        SeparateFolder,
        SameFolderAsOriginal
    }

    public enum PhysBonePruningStrategy
    {
        Disabled,
        DeepestFirst,
        ShallowestFirst,
        InteractiveChecklist
    }

    /// <summary>
    /// Base class for platform performance profiles defining resource and component limits
    /// according to official VRChat SDK Performance Rank specifications.
    /// </summary>
    [Serializable]
    public abstract class PlatformProfile
    {
        public abstract TargetPlatform Platform { get; }
        public abstract AvatarPerformanceRank Rank { get; }

        // Geometry & Mesh Limits
        public int MaxTriangles = int.MaxValue;
        public int MaxSkinnedMeshes = int.MaxValue;
        public int MaxMeshRenderers = int.MaxValue;
        public int MaxMaterialSlots = int.MaxValue;
        public int MaxBones = int.MaxValue;
        public int MaxAnimators = int.MaxValue;
        public Vector3 MaxBoundsSize = new Vector3(5f, 6f, 5f);

        // Texture & Memory Limits
        public long MaxTextureMemoryBytes = 40 * 1024 * 1024L;

        // PhysBone Limits
        public int MaxPhysBoneComponents = 8;
        public int MaxPhysBoneTransforms = 64;
        public int MaxPhysBoneColliders = 16;
        public int MaxPhysBoneCollisionChecks = 64;

        // Particle System Limits
        public int MaxMeshParticlePolyCount = int.MaxValue;
        public int MaxParticleSystems = int.MaxValue;

        // Lights & Audio
        public int MaxLights = int.MaxValue;
        public int MaxAudioSources = int.MaxValue;

        // Component Whitelists & Blacklists
        public HashSet<string> WhitelistedComponentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> BlacklistedComponentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Performs custom, platform-specific component compatibility check.
        /// Returns true if component should be removed.
        /// </summary>
        public virtual bool ShouldRemoveComponentCustom(Component comp)
        {
            return false;
        }

        /// <summary>
        /// Executes custom platform-specific optimization and conversion operations on the target avatar.
        /// </summary>
        public virtual void ExecutePlatformConversions(GameObject avatarRoot, System.Action<string> progressCallback = null)
        {
        }

        /// <summary>
        /// Validates platform-specific requirements and reports issues or warnings.
        /// </summary>
        public virtual void ValidatePlatformRules(GameObject avatarRoot, ConversionSummary summary)
        {
        }

        public static PlatformProfile GetProfile(TargetPlatform platform, AvatarPerformanceRank rank)
        {
            if (platform == TargetPlatform.PC)
            {
                switch (rank)
                {
                    case AvatarPerformanceRank.Excellent: return new PC_Excellent_Profile();
                    case AvatarPerformanceRank.Good: return new PC_Good_Profile();
                    case AvatarPerformanceRank.Medium: return new PC_Medium_Profile();
                    case AvatarPerformanceRank.Poor: return new PC_Poor_Profile();
                    case AvatarPerformanceRank.VeryPoor:
                    default: return new PC_VeryPoor_Profile();
                }
            }
            else if (platform == TargetPlatform.iOS)
            {
                switch (rank)
                {
                    case AvatarPerformanceRank.Excellent: return new iOS_Excellent_Profile();
                    case AvatarPerformanceRank.Good: return new iOS_Good_Profile();
                    case AvatarPerformanceRank.Medium: return new iOS_Medium_Profile();
                    case AvatarPerformanceRank.Poor: return new iOS_Poor_Profile();
                    case AvatarPerformanceRank.VeryPoor:
                    default: return new iOS_VeryPoor_Profile();
                }
            }
            else
            {
                switch (rank)
                {
                    case AvatarPerformanceRank.Excellent: return new Android_Excellent_Profile();
                    case AvatarPerformanceRank.Good: return new Android_Good_Profile();
                    case AvatarPerformanceRank.Medium: return new Android_Medium_Profile();
                    case AvatarPerformanceRank.Poor: return new Android_Poor_Profile();
                    case AvatarPerformanceRank.VeryPoor:
                    default: return new Android_VeryPoor_Profile();
                }
            }
        }
    }

    // =========================================================================
    // BASE PLATFORM PROFILES
    // =========================================================================
    public abstract class PC_PlatformProfile_Base : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.PC;
    }

    public abstract class Android_PlatformProfile_Base : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.Android;

        protected Android_PlatformProfile_Base()
        {
            // All Android profiles strictly blacklist components prohibited by VRChat Quest SDK policy
            BlacklistedComponentNames.UnionWith(new[] {
                "Cloth", "Camera", "Light", "AudioSource", "Rigidbody", "Joint", "SpringJoint", "HingeJoint",
                "FixedJoint", "CharacterJoint", "ConfigurableJoint", "ParticleSystem", "DynamicBone",
                "DynamicBoneCollider", "VRCSpatialAudioSource", "FinalIK", "PostProcessLayer", "PostProcessVolume"
            });
        }

        public override bool ShouldRemoveComponentCustom(Component comp)
        {
            if (comp == null) return false;
            System.Type t = comp.GetType();

            // Mobile-specific custom checks: remove custom post-processing & non-mobile shaders
            if (t.Name.Contains("PostProcess") || t.Name.Contains("VRCContact")) return false; // Handled separately
            return base.ShouldRemoveComponentCustom(comp);
        }

        public override void ExecutePlatformConversions(GameObject avatarRoot, System.Action<string> progressCallback = null)
        {
            progressCallback?.Invoke("Executing Android platform-specific conversions...");
            // Additional Android/Quest specific logic (e.g., stripping lightmaps, enforcing GPU instancing)
        }

        public override void ValidatePlatformRules(GameObject avatarRoot, ConversionSummary summary)
        {
            if (avatarRoot == null || summary == null) return;
            // Android platform validation checks
            var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r != null && r.sharedMaterials != null && r.sharedMaterials.Length > 4)
                {
                    summary.AddWarning($"Renderer '{r.name}' has {r.sharedMaterials.Length} material slots (Android Quest limit is 4 per avatar).", r);
                }
            }
        }
    }

    public abstract class iOS_PlatformProfile_Base : Android_PlatformProfile_Base
    {
        public override TargetPlatform Platform => TargetPlatform.iOS;

        public override void ExecutePlatformConversions(GameObject avatarRoot, System.Action<string> progressCallback = null)
        {
            progressCallback?.Invoke("Executing iOS mobile platform-specific conversions...");
        }
    }

    // =========================================================================
    // PC PLATFORM PROFILES (Official VRChat PC Performance Limits)
    // =========================================================================
    public class PC_Excellent_Profile : PC_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Excellent;
        public PC_Excellent_Profile()
        {
            MaxTriangles = 32000;
            MaxSkinnedMeshes = 1;
            MaxMeshRenderers = 4;
            MaxMaterialSlots = 8;
            MaxBones = 75;
            MaxAnimators = 1;
            MaxBoundsSize = new Vector3(2.5f, 2.5f, 2.5f);
            MaxTextureMemoryBytes = 40 * 1024 * 1024L;
            MaxPhysBoneComponents = 4;
            MaxPhysBoneTransforms = 16;
            MaxPhysBoneColliders = 4;
            MaxPhysBoneCollisionChecks = 16;
            MaxMeshParticlePolyCount = 1000;
            MaxParticleSystems = 2;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }

    public class PC_Good_Profile : PC_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Good;
        public PC_Good_Profile()
        {
            MaxTriangles = 70000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 8;
            MaxMaterialSlots = 16;
            MaxBones = 150;
            MaxAnimators = 2;
            MaxBoundsSize = new Vector3(4f, 4f, 4f);
            MaxTextureMemoryBytes = 75 * 1024 * 1024L;
            MaxPhysBoneComponents = 8;
            MaxPhysBoneTransforms = 32;
            MaxPhysBoneColliders = 8;
            MaxPhysBoneCollisionChecks = 32;
            MaxMeshParticlePolyCount = 2000;
            MaxParticleSystems = 4;
            MaxLights = 1;
            MaxAudioSources = 1;
        }
    }

    public class PC_Medium_Profile : PC_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Medium;
        public PC_Medium_Profile()
        {
            MaxTriangles = 70000;
            MaxSkinnedMeshes = 8;
            MaxMeshRenderers = 16;
            MaxMaterialSlots = 32;
            MaxBones = 250;
            MaxAnimators = 4;
            MaxBoundsSize = new Vector3(5f, 6f, 5f);
            MaxTextureMemoryBytes = 150 * 1024 * 1024L;
            MaxPhysBoneComponents = 16;
            MaxPhysBoneTransforms = 64;
            MaxPhysBoneColliders = 16;
            MaxPhysBoneCollisionChecks = 64;
            MaxMeshParticlePolyCount = 5000;
            MaxParticleSystems = 8;
            MaxLights = 1;
            MaxAudioSources = 1;
        }
    }

    public class PC_Poor_Profile : PC_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Poor;
        public PC_Poor_Profile()
        {
            MaxTriangles = 70000;
            MaxSkinnedMeshes = 16;
            MaxMeshRenderers = 32;
            MaxMaterialSlots = 64;
            MaxBones = 400;
            MaxAnimators = 8;
            MaxBoundsSize = new Vector3(5f, 6f, 5f);
            MaxTextureMemoryBytes = 300 * 1024 * 1024L;
            MaxPhysBoneComponents = 32;
            MaxPhysBoneTransforms = 128;
            MaxPhysBoneColliders = 32;
            MaxPhysBoneCollisionChecks = 128;
            MaxMeshParticlePolyCount = 10000;
            MaxParticleSystems = 16;
            MaxLights = 2;
            MaxAudioSources = 2;
        }
    }

    public class PC_VeryPoor_Profile : PC_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.VeryPoor;
        public PC_VeryPoor_Profile()
        {
            MaxTriangles = int.MaxValue;
            MaxSkinnedMeshes = int.MaxValue;
            MaxMeshRenderers = int.MaxValue;
            MaxMaterialSlots = int.MaxValue;
            MaxBones = int.MaxValue;
            MaxAnimators = int.MaxValue;
            MaxTextureMemoryBytes = 500 * 1024 * 1024L;
            MaxPhysBoneComponents = int.MaxValue;
            MaxPhysBoneTransforms = int.MaxValue;
            MaxPhysBoneColliders = int.MaxValue;
            MaxPhysBoneCollisionChecks = int.MaxValue;
            MaxMeshParticlePolyCount = int.MaxValue;
            MaxParticleSystems = int.MaxValue;
            MaxLights = int.MaxValue;
            MaxAudioSources = int.MaxValue;
        }
    }

    // =========================================================================
    // ANDROID / QUEST PLATFORM PROFILES (Official VRChat Android Limits)
    // =========================================================================
    public class Android_Excellent_Profile : Android_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Excellent;
        public Android_Excellent_Profile()
        {
            MaxTriangles = 7500;
            MaxSkinnedMeshes = 1;
            MaxMeshRenderers = 1;
            MaxMaterialSlots = 1;
            MaxBones = 75;
            MaxAnimators = 1;
            MaxBoundsSize = new Vector3(2.5f, 2.5f, 2.5f);
            MaxTextureMemoryBytes = 10 * 1024 * 1024L;
            MaxPhysBoneComponents = 0;
            MaxPhysBoneTransforms = 0;
            MaxPhysBoneColliders = 0;
            MaxPhysBoneCollisionChecks = 0;
            MaxMeshParticlePolyCount = 0;
            MaxParticleSystems = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }

    public class Android_Good_Profile : Android_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Good;
        public Android_Good_Profile()
        {
            MaxTriangles = 15000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 2;
            MaxMaterialSlots = 2;
            MaxBones = 90;
            MaxAnimators = 1;
            MaxBoundsSize = new Vector3(4f, 4f, 4f);
            MaxTextureMemoryBytes = 10 * 1024 * 1024L;
            MaxPhysBoneComponents = 8;
            MaxPhysBoneTransforms = 16;
            MaxPhysBoneColliders = 8;
            MaxPhysBoneCollisionChecks = 16;
            MaxMeshParticlePolyCount = 0;
            MaxParticleSystems = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }

    public class Android_Medium_Profile : Android_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Medium;
        public Android_Medium_Profile()
        {
            MaxTriangles = 20000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 2;
            MaxMaterialSlots = 4;
            MaxBones = 150;
            MaxAnimators = 1;
            MaxBoundsSize = new Vector3(5f, 6f, 5f);
            MaxTextureMemoryBytes = 20 * 1024 * 1024L;
            MaxPhysBoneComponents = 8;
            MaxPhysBoneTransforms = 32;
            MaxPhysBoneColliders = 16;
            MaxPhysBoneCollisionChecks = 32;
            MaxMeshParticlePolyCount = 0;
            MaxParticleSystems = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }

    public class Android_Poor_Profile : Android_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Poor;
        public Android_Poor_Profile()
        {
            MaxTriangles = 20000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 2;
            MaxMaterialSlots = 4;
            MaxBones = 150;
            MaxAnimators = 1;
            MaxBoundsSize = new Vector3(5f, 6f, 5f);
            MaxTextureMemoryBytes = 40 * 1024 * 1024L;
            MaxPhysBoneComponents = 8;
            MaxPhysBoneTransforms = 64;
            MaxPhysBoneColliders = 16;
            MaxPhysBoneCollisionChecks = 64;
            MaxMeshParticlePolyCount = 0;
            MaxParticleSystems = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }

    public class Android_VeryPoor_Profile : Android_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.VeryPoor;
        public Android_VeryPoor_Profile()
        {
            MaxTriangles = int.MaxValue;
            MaxSkinnedMeshes = int.MaxValue;
            MaxMeshRenderers = int.MaxValue;
            MaxMaterialSlots = int.MaxValue;
            MaxBones = int.MaxValue;
            MaxAnimators = int.MaxValue;
            MaxTextureMemoryBytes = 40 * 1024 * 1024L; // Quest VRAM hard limit
            MaxPhysBoneComponents = 8;                  // Quest PhysBone script cap
            MaxPhysBoneTransforms = 64;                 // Quest PhysBone transform cap
            MaxPhysBoneColliders = 16;                  // Quest PhysBone collider cap
            MaxPhysBoneCollisionChecks = 64;            // Quest PhysBone collision check cap
            MaxMeshParticlePolyCount = 0;
            MaxParticleSystems = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }

    // =========================================================================
    // IOS PLATFORM PROFILES (Official VRChat iOS Mobile Limits - identical to Mobile/Android)
    // =========================================================================
    public class iOS_Excellent_Profile : iOS_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Excellent;
        public iOS_Excellent_Profile()
        {
            MaxTriangles = 7500;
            MaxSkinnedMeshes = 1;
            MaxMeshRenderers = 1;
            MaxMaterialSlots = 1;
            MaxBones = 75;
            MaxAnimators = 1;
            MaxBoundsSize = new Vector3(2.5f, 2.5f, 2.5f);
            MaxTextureMemoryBytes = 10 * 1024 * 1024L;
            MaxPhysBoneComponents = 0;
            MaxPhysBoneTransforms = 0;
            MaxPhysBoneColliders = 0;
            MaxPhysBoneCollisionChecks = 0;
            MaxMeshParticlePolyCount = 0;
            MaxParticleSystems = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }

    public class iOS_Good_Profile : iOS_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Good;
        public iOS_Good_Profile()
        {
            MaxTriangles = 15000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 2;
            MaxMaterialSlots = 2;
            MaxBones = 90;
            MaxAnimators = 1;
            MaxBoundsSize = new Vector3(4f, 4f, 4f);
            MaxTextureMemoryBytes = 10 * 1024 * 1024L;
            MaxPhysBoneComponents = 8;
            MaxPhysBoneTransforms = 16;
            MaxPhysBoneColliders = 8;
            MaxPhysBoneCollisionChecks = 16;
            MaxMeshParticlePolyCount = 0;
            MaxParticleSystems = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }

    public class iOS_Medium_Profile : iOS_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Medium;
        public iOS_Medium_Profile()
        {
            MaxTriangles = 20000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 2;
            MaxMaterialSlots = 4;
            MaxBones = 150;
            MaxAnimators = 1;
            MaxBoundsSize = new Vector3(5f, 6f, 5f);
            MaxTextureMemoryBytes = 20 * 1024 * 1024L;
            MaxPhysBoneComponents = 8;
            MaxPhysBoneTransforms = 32;
            MaxPhysBoneColliders = 16;
            MaxPhysBoneCollisionChecks = 32;
            MaxMeshParticlePolyCount = 0;
            MaxParticleSystems = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }

    public class iOS_Poor_Profile : iOS_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Poor;
        public iOS_Poor_Profile()
        {
            MaxTriangles = 20000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 2;
            MaxMaterialSlots = 4;
            MaxBones = 150;
            MaxAnimators = 1;
            MaxBoundsSize = new Vector3(5f, 6f, 5f);
            MaxTextureMemoryBytes = 40 * 1024 * 1024L;
            MaxPhysBoneComponents = 8;
            MaxPhysBoneTransforms = 64;
            MaxPhysBoneColliders = 16;
            MaxPhysBoneCollisionChecks = 64;
            MaxMeshParticlePolyCount = 0;
            MaxParticleSystems = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }

    public class iOS_VeryPoor_Profile : iOS_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.VeryPoor;
        public iOS_VeryPoor_Profile()
        {
            MaxTriangles = int.MaxValue;
            MaxSkinnedMeshes = int.MaxValue;
            MaxMeshRenderers = int.MaxValue;
            MaxMaterialSlots = int.MaxValue;
            MaxBones = int.MaxValue;
            MaxAnimators = int.MaxValue;
            MaxTextureMemoryBytes = 40 * 1024 * 1024L;
            MaxPhysBoneComponents = 8;
            MaxPhysBoneTransforms = 64;
            MaxPhysBoneColliders = 16;
            MaxPhysBoneCollisionChecks = 64;
            MaxMeshParticlePolyCount = 0;
            MaxParticleSystems = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }
}
