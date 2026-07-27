using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public class PlatformProfile_Android_Good : PlatformProfile_Android
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.Good;
        public PlatformProfile_Android_Good()
        {
            MaxTriangles = 10000;
            MaxSkinnedMeshes = 1;
            MaxMeshRenderers = 1;
            MaxMaterialSlots = 1;
            MaxBones = 90;
            MaxAnimators = 1;
            MaxBoundsSize = new Vector3(4f, 4f, 4f);
            MaxTextureMemoryBytes = 18 * 1024 * 1024L; // 18 MB
            MaxPhysBoneComponents = 4;
            MaxPhysBoneTransforms = 16;
            MaxPhysBoneColliders = 4;
            MaxPhysBoneCollisionChecks = 16;
            MaxContacts = 4;
            MaxConstraints = 60;
            MaxConstraintDepth = 15;
            MaxParticleSystems = 0;
            MaxActiveParticles = 0;
            MaxMeshParticlePolyCount = 0;
            ParticleTrailsEnabledAllowed = false;
            ParticleCollisionEnabledAllowed = false;
            MaxTrailRenderers = 0;
            MaxLineRenderers = 0;
            MaxRaycasts = 2;
            MaxClothComponents = 0;
            MaxClothVertices = 0;
            MaxPhysicsColliders = 0;
            MaxRigidbodies = 0;
            MaxLights = 0;
            MaxAudioSources = 0;
        }
    }
}
