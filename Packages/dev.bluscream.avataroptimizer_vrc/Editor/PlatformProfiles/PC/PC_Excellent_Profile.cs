using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
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
}
