namespace Bluscream.VRCAvatarOptimizer
{
    public class PlatformProfile_PC_VeryPoor : PlatformProfile_PC
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.VeryPoor;
        public PlatformProfile_PC_VeryPoor()
        {
            MaxTriangles = int.MaxValue;
            MaxSkinnedMeshes = int.MaxValue;
            MaxMeshRenderers = int.MaxValue;
            MaxMaterialSlots = int.MaxValue;
            MaxBones = int.MaxValue;
            MaxAnimators = int.MaxValue;
            MaxTextureMemoryBytes = 500 * 1024 * 1024L; // 500 MB
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
}
