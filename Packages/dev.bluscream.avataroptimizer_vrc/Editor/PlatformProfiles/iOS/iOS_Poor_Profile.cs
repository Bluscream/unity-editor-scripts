using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public class iOS_Poor_Profile : iOS_PlatformProfile_Base
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Poor;
        public iOS_Poor_Profile()
        {
            MaxTriangles = 20000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 2;
            MaxMaterialSlots = 4;
            MaxBones = 150;
            MaxAnimators = 1;
            MaxBoundsSize = new Vector3(5f, 6f, 5f);
            MaxTextureMemoryBytes = 40 * 1024 * 1024L;
            MaxPhysBoneComponents = 8;
            MaxPhysBoneTransforms = 64;
            MaxPhysBoneColliders = 16;
            MaxPhysBoneCollisionChecks = 64;
            MaxMeshParticlePolyCount = 0;
            MaxParticleSystems = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }
}
