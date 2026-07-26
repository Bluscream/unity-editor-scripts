using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public class PlatformProfile_iOS_Excellent : PlatformProfile_iOS
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Excellent;
        public PlatformProfile_iOS_Excellent()
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
}
