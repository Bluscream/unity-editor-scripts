using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public class iOS_Good_Profile : iOS_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Good;
        public iOS_Good_Profile()
        {
            MaxTriangles = 15000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 2;
            MaxMaterialSlots = 2;
            MaxBones = 90;
            MaxAnimators = 1;
            MaxBoundsSize = new Vector3(4f, 4f, 4f);
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
}
