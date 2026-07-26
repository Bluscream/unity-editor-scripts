namespace Bluscream.VRCAvatarOptimizer
{
    public class PlatformProfile_iOS_VeryPoor : PlatformProfile_iOS
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.VeryPoor;
        public PlatformProfile_iOS_VeryPoor()
        {
            MaxTriangles = int.MaxValue;
            MaxSkinnedMeshes = int.MaxValue;
            MaxMeshRenderers = int.MaxValue;
            MaxMaterialSlots = int.MaxValue;
            MaxBones = int.MaxValue;
            MaxAnimators = int.MaxValue;
            MaxTextureMemoryBytes = 40 * 1024 * 1024L; // VRChat Mobile VRAM hard limit
            MaxPhysBoneComponents = 8;                  // VRChat Mobile PhysBone script cap
            MaxPhysBoneTransforms = 64;                 // VRChat Mobile PhysBone transform cap
            MaxPhysBoneColliders = 16;                  // VRChat Mobile PhysBone collider cap
            MaxPhysBoneCollisionChecks = 64;            // VRChat Mobile PhysBone collision check cap
            MaxMeshParticlePolyCount = 0;
            MaxParticleSystems = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }
}
