using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public class PlatformProfile_PC_Excellent : PlatformProfile_PC
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Excellent;
        public PlatformProfile_PC_Excellent()
        {
            MaxTriangles = 32000;
            MaxSkinnedMeshes = 1;
            MaxMeshRenderers = 4;
            MaxMaterialSlots = 4;
            MaxBones = 75;
            MaxAnimators = 1;
            MaxBoundsSize = new Vector3(2.5f, 2.5f, 2.5f);
            MaxTextureMemoryBytes = 40 * 1024 * 1024L; // 40 MB
            MaxPhysBoneComponents = 4;
            MaxPhysBoneTransforms = 16;
            MaxPhysBoneColliders = 4;
            MaxPhysBoneCollisionChecks = 32;
            MaxContacts = 8;
            MaxConstraints = 100;
            MaxConstraintDepth = 20;
            MaxParticleSystems = 0;
            MaxActiveParticles = 0;
            MaxMeshParticlePolyCount = 0;
            ParticleTrailsEnabledAllowed = false;
            ParticleCollisionEnabledAllowed = false;
            MaxTrailRenderers = 1;
            MaxLineRenderers = 1;
            MaxRaycasts = 1;
            MaxClothComponents = 0;
            MaxClothVertices = 0;
            MaxPhysicsColliders = 0;
            MaxRigidbodies = 0;
            MaxLights = 0;
            MaxAudioSources = 1;
        }
    }
}
