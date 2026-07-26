namespace Bluscream.VRCAvatarOptimizer
{
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
}
