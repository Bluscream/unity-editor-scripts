using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
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
}
