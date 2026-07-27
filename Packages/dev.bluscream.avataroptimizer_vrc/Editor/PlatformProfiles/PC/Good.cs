using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public class PlatformProfile_PC_Good : PlatformProfile_PC
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Good;
        public PlatformProfile_PC_Good()
        {
            MaxTriangles = 70000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 8;
            MaxMaterialSlots = 8;
            MaxBones = 150;
            MaxAnimators = 4;
            MaxBoundsSize = new Vector3(4f, 4f, 4f);
            MaxTextureMemoryBytes = 75 * 1024 * 1024L; // 75 MB
            MaxPhysBoneComponents = 8;
            MaxPhysBoneTransforms = 64;
            MaxPhysBoneColliders = 8;
            MaxPhysBoneCollisionChecks = 128;
            MaxContacts = 16;
            MaxConstraints = 250;
            MaxConstraintDepth = 50;
            MaxParticleSystems = 4;
            MaxActiveParticles = 300;
            MaxMeshParticlePolyCount = 1000;
            ParticleTrailsEnabledAllowed = false;
            ParticleCollisionEnabledAllowed = false;
            MaxTrailRenderers = 2;
            MaxLineRenderers = 2;
            MaxRaycasts = 4;
            MaxClothComponents = 1;
            MaxClothVertices = 50;
            MaxPhysicsColliders = 1;
            MaxRigidbodies = 1;
            MaxLights = 0;
            MaxAudioSources = 4;
        }
    }
}
