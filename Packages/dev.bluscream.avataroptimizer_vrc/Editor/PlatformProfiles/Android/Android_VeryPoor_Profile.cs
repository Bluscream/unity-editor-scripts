namespace Bluscream.VRCAvatarOptimizer
{
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
}
