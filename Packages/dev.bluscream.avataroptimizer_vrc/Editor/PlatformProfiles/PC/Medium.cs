using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public class PlatformProfile_PC_Medium : PlatformProfile_PC
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Medium;
        public PlatformProfile_PC_Medium()
        {
            MaxTriangles = 70000;
            MaxSkinnedMeshes = 8;
            MaxMeshRenderers = 16;
            MaxMaterialSlots = 16;
            MaxBones = 256;
            MaxAnimators = 16;
            MaxBoundsSize = new Vector3(5f, 6f, 5f);
            MaxTextureMemoryBytes = 110 * 1024 * 1024L; // 110 MB
            MaxPhysBoneComponents = 16;
            MaxPhysBoneTransforms = 128;
            MaxPhysBoneColliders = 16;
            MaxPhysBoneCollisionChecks = 256;
            MaxContacts = 24;
            MaxConstraints = 300;
            MaxConstraintDepth = 80;
            MaxParticleSystems = 8;
            MaxActiveParticles = 1000;
            MaxMeshParticlePolyCount = 2000;
            ParticleTrailsEnabledAllowed = true;
            ParticleCollisionEnabledAllowed = true;
            MaxTrailRenderers = 4;
            MaxLineRenderers = 4;
            MaxRaycasts = 8;
            MaxClothComponents = 1;
            MaxClothVertices = 100;
            MaxPhysicsColliders = 8;
            MaxRigidbodies = 8;
            MaxLights = 0;
            MaxAudioSources = 8;
        }
    }
}
