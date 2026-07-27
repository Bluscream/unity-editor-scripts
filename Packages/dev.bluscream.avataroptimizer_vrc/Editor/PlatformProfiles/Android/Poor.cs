using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public class PlatformProfile_Android_Poor : PlatformProfile_Android
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Poor;
        public PlatformProfile_Android_Poor()
        {
            MaxTriangles = 20000;
            MaxSkinnedMeshes = 2;
            MaxMeshRenderers = 2;
            MaxMaterialSlots = 4;
            MaxBones = 150;
            MaxAnimators = 2;
            MaxBoundsSize = new Vector3(5f, 6f, 5f);
            MaxTextureMemoryBytes = 40 * 1024 * 1024L; // 40 MB
            MaxPhysBoneComponents = 8;
            MaxPhysBoneTransforms = 64;
            MaxPhysBoneColliders = 16;
            MaxPhysBoneCollisionChecks = 64;
            MaxContacts = 16;
            MaxConstraints = 150;
            MaxConstraintDepth = 50;
            MaxParticleSystems = 2;
            MaxActiveParticles = 200;
            MaxMeshParticlePolyCount = 400;
            ParticleTrailsEnabledAllowed = true;
            ParticleCollisionEnabledAllowed = true;
            MaxTrailRenderers = 1;
            MaxLineRenderers = 1;
            MaxRaycasts = 8;
            MaxClothComponents = 0;
            MaxClothVertices = 0;
            MaxPhysicsColliders = 0;
            MaxRigidbodies = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }
}
