using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
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
}
