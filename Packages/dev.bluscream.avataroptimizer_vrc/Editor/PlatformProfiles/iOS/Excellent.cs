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
            MaxTextureMemoryBytes = 10 * 1024 * 1024L; // 10 MB
            MaxPhysBoneComponents = 0;
            MaxPhysBoneTransforms = 0;
            MaxPhysBoneColliders = 0;
            MaxPhysBoneCollisionChecks = 0;
            MaxContacts = 2;
            MaxConstraints = 30;
            MaxConstraintDepth = 5;
            MaxParticleSystems = 0;
            MaxActiveParticles = 0;
            MaxMeshParticlePolyCount = 0;
            ParticleTrailsEnabledAllowed = false;
            ParticleCollisionEnabledAllowed = false;
            MaxTrailRenderers = 0;
            MaxLineRenderers = 0;
            MaxRaycasts = 1;
            MaxClothComponents = 0;
            MaxClothVertices = 0;
            MaxPhysicsColliders = 0;
            MaxRigidbodies = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }
}
