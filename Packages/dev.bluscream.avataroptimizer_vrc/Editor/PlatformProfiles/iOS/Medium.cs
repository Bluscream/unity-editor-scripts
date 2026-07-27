using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public class PlatformProfile_iOS_Medium : PlatformProfile_iOS
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Medium;
        public PlatformProfile_iOS_Medium()
        {
            MaxTriangles = 15000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 2;
            MaxMaterialSlots = 2;
            MaxBones = 150;
            MaxAnimators = 1;
            MaxBoundsSize = new Vector3(5f, 6f, 5f);
            MaxTextureMemoryBytes = 25 * 1024 * 1024L; // 25 MB
            MaxPhysBoneComponents = 6;
            MaxPhysBoneTransforms = 32;
            MaxPhysBoneColliders = 8;
            MaxPhysBoneCollisionChecks = 32;
            MaxContacts = 8;
            MaxConstraints = 120;
            MaxConstraintDepth = 35;
            MaxParticleSystems = 0;
            MaxActiveParticles = 0;
            MaxMeshParticlePolyCount = 0;
            ParticleTrailsEnabledAllowed = false;
            ParticleCollisionEnabledAllowed = false;
            MaxTrailRenderers = 0;
            MaxLineRenderers = 0;
            MaxRaycasts = 4;
            MaxClothComponents = 0;
            MaxClothVertices = 0;
            MaxPhysicsColliders = 0;
            MaxRigidbodies = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }
}
