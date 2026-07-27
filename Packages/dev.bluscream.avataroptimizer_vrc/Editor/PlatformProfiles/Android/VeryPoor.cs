namespace Bluscream.VRCAvatarOptimizer
{
    public class PlatformProfile_Android_VeryPoor : PlatformProfile_Android
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.VeryPoor;
        public PlatformProfile_Android_VeryPoor()
        {
            MaxTriangles = int.MaxValue;
            MaxSkinnedMeshes = int.MaxValue;
            MaxMeshRenderers = int.MaxValue;
            MaxMaterialSlots = int.MaxValue;
            MaxBones = int.MaxValue;
            MaxAnimators = int.MaxValue;
            MaxTextureMemoryBytes = 40 * 1024 * 1024L; // 40 MB (Quest VRAM hard limit)
            MaxPhysBoneComponents = 8;                  // Quest PhysBone script cap
            MaxPhysBoneTransforms = 64;                 // Quest PhysBone transform cap
            MaxPhysBoneColliders = 16;                  // Quest PhysBone collider cap
            MaxPhysBoneCollisionChecks = 64;            // Quest PhysBone collision check cap
            MaxContacts = 16;                           // Quest Contact cap
            MaxConstraints = 150;                       // Quest Constraint cap
            MaxConstraintDepth = 50;                    // Quest Constraint depth cap
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
