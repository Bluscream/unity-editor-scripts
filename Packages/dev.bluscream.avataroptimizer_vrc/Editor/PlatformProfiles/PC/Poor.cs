using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public class PlatformProfile_PC_Poor : PlatformProfile_PC
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Poor;
        public PlatformProfile_PC_Poor()
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
}
