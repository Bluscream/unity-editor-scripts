using System;
using UnityEngine;

namespace VRCQuestPatcher
{
    public enum TargetPlatform
    {
        PC,
        Android
    }

    public enum QuestPerformanceRank
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
        public abstract QuestPerformanceRank Rank { get; }

        // Geometry & Mesh Limits
        public int MaxTriangles = int.MaxValue;
        public int MaxSkinnedMeshes = int.MaxValue;
        public int MaxMeshRenderers = int.MaxValue;
        public int MaxMaterialSlots = int.MaxValue;
        public int MaxBones = int.MaxValue;
        public int MaxAnimators = int.MaxValue;

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

        public static PlatformProfile GetProfile(TargetPlatform platform, QuestPerformanceRank rank)
        {
            if (platform == TargetPlatform.PC)
            {
                switch (rank)
                {
                    case QuestPerformanceRank.Excellent: return new PC_Excellent_Profile();
                    case QuestPerformanceRank.Good: return new PC_Good_Profile();
                    case QuestPerformanceRank.Medium: return new PC_Medium_Profile();
                    case QuestPerformanceRank.Poor: return new PC_Poor_Profile();
                    case QuestPerformanceRank.VeryPoor:
                    default: return new PC_VeryPoor_Profile();
                }
            }
            else
            {
                switch (rank)
                {
                    case QuestPerformanceRank.Excellent: return new Android_Excellent_Profile();
                    case QuestPerformanceRank.Good: return new Android_Good_Profile();
                    case QuestPerformanceRank.Medium: return new Android_Medium_Profile();
                    case QuestPerformanceRank.Poor: return new Android_Poor_Profile();
                    case QuestPerformanceRank.VeryPoor:
                    default: return new Android_VeryPoor_Profile();
                }
            }
        }
    }

    // =========================================================================
    // PC PLATFORM PROFILES (Official VRChat PC Performance Limits)
    // =========================================================================
    public class PC_Excellent_Profile : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.PC;
        public override QuestPerformanceRank Rank => QuestPerformanceRank.Excellent;
        public PC_Excellent_Profile()
        {
            MaxTriangles = 32000;
            MaxSkinnedMeshes = 1;
            MaxMeshRenderers = 4;
            MaxMaterialSlots = 8;
            MaxBones = 75;
            MaxAnimators = 1;
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

    public class PC_Good_Profile : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.PC;
        public override QuestPerformanceRank Rank => QuestPerformanceRank.Good;
        public PC_Good_Profile()
        {
            MaxTriangles = 70000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 8;
            MaxMaterialSlots = 16;
            MaxBones = 150;
            MaxAnimators = 2;
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

    public class PC_Medium_Profile : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.PC;
        public override QuestPerformanceRank Rank => QuestPerformanceRank.Medium;
        public PC_Medium_Profile()
        {
            MaxTriangles = 70000;
            MaxSkinnedMeshes = 8;
            MaxMeshRenderers = 16;
            MaxMaterialSlots = 32;
            MaxBones = 250;
            MaxAnimators = 4;
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

    public class PC_Poor_Profile : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.PC;
        public override QuestPerformanceRank Rank => QuestPerformanceRank.Poor;
        public PC_Poor_Profile()
        {
            MaxTriangles = 70000;
            MaxSkinnedMeshes = 16;
            MaxMeshRenderers = 32;
            MaxMaterialSlots = 64;
            MaxBones = 400;
            MaxAnimators = 8;
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

    public class PC_VeryPoor_Profile : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.PC;
        public override QuestPerformanceRank Rank => QuestPerformanceRank.VeryPoor;
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
    public class Android_Excellent_Profile : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.Android;
        public override QuestPerformanceRank Rank => QuestPerformanceRank.Excellent;
        public Android_Excellent_Profile()
        {
            MaxTriangles = 7500;
            MaxSkinnedMeshes = 1;
            MaxMeshRenderers = 1;
            MaxMaterialSlots = 1;
            MaxBones = 75;
            MaxAnimators = 1;
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

    public class Android_Good_Profile : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.Android;
        public override QuestPerformanceRank Rank => QuestPerformanceRank.Good;
        public Android_Good_Profile()
        {
            MaxTriangles = 15000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 2;
            MaxMaterialSlots = 2;
            MaxBones = 90;
            MaxAnimators = 1;
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

    public class Android_Medium_Profile : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.Android;
        public override QuestPerformanceRank Rank => QuestPerformanceRank.Medium;
        public Android_Medium_Profile()
        {
            MaxTriangles = 20000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 2;
            MaxMaterialSlots = 4;
            MaxBones = 150;
            MaxAnimators = 1;
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

    public class Android_Poor_Profile : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.Android;
        public override QuestPerformanceRank Rank => QuestPerformanceRank.Poor;
        public Android_Poor_Profile()
        {
            MaxTriangles = 20000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 2;
            MaxMaterialSlots = 4;
            MaxBones = 150;
            MaxAnimators = 1;
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

    public class Android_VeryPoor_Profile : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.Android;
        public override QuestPerformanceRank Rank => QuestPerformanceRank.VeryPoor;
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

    // Legacy alias for backward compatibility
    [Obsolete("Use PlatformProfile instead.")]
    public class QuestPerformanceProfile : Android_VeryPoor_Profile
    {
        public static QuestPerformanceProfile GetProfile(QuestPerformanceRank rank)
        {
            var p = PlatformProfile.GetProfile(TargetPlatform.Android, rank);
            var q = new QuestPerformanceProfile();
            q.MaxTriangles = p.MaxTriangles;
            q.MaxSkinnedMeshes = p.MaxSkinnedMeshes;
            q.MaxMeshRenderers = p.MaxMeshRenderers;
            q.MaxMaterialSlots = p.MaxMaterialSlots;
            q.MaxTextureMemoryBytes = p.MaxTextureMemoryBytes;
            q.MaxPhysBoneComponents = p.MaxPhysBoneComponents;
            q.MaxPhysBoneTransforms = p.MaxPhysBoneTransforms;
            q.MaxPhysBoneColliders = p.MaxPhysBoneColliders;
            q.MaxPhysBoneCollisionChecks = p.MaxPhysBoneCollisionChecks;
            return q;
        }
    }
}
