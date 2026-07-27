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
            MaxMeshRenderers = 24;
            MaxMaterialSlots = 32;
            MaxBones = 400;
            MaxAnimators = 32;
            MaxBoundsSize = new Vector3(5f, 6f, 5f);
            MaxTextureMemoryBytes = 150 * 1024 * 1024L; // 150 MB
            MaxPhysBoneComponents = 32;
            MaxPhysBoneTransforms = 256;
            MaxPhysBoneColliders = 32;
            MaxPhysBoneCollisionChecks = 512;
            MaxContacts = 32;
            MaxConstraints = 350;
            MaxConstraintDepth = 100;
            MaxParticleSystems = 16;
            MaxActiveParticles = 2500;
            MaxMeshParticlePolyCount = 5000;
            ParticleTrailsEnabledAllowed = true;
            ParticleCollisionEnabledAllowed = true;
            MaxTrailRenderers = 8;
            MaxLineRenderers = 8;
            MaxRaycasts = 15;
            MaxClothComponents = 1;
            MaxClothVertices = 200;
            MaxPhysicsColliders = 8;
            MaxRigidbodies = 8;
            MaxLights = 1;
            MaxAudioSources = 8;
        }
    }
}
