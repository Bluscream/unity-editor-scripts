namespace Bluscream.VRCAvatarOptimizer
{
    public class PlatformProfile_iOS_VeryPoor : PlatformProfile_iOS
    {
        public override AvatarPerformanceRank Rank => AvatarPerformanceRank.VeryPoor;
        public PlatformProfile_iOS_VeryPoor()
        {
            MaxTriangles = int.MaxValue;
            MaxSkinnedMeshes = int.MaxValue;
            MaxMeshRenderers = int.MaxValue;
            MaxMaterialSlots = int.MaxValue;
            MaxBones = int.MaxValue;
            MaxAnimators = int.MaxValue;
            MaxTextureMemoryBytes = 40 * 1024 * 1024L; // 40 MB (VRChat Mobile VRAM hard limit)
            MaxPhysBoneComponents = 8;                  // VRChat Mobile PhysBone script cap
            MaxPhysBoneTransforms = 64;                 // VRChat Mobile PhysBone transform cap
            MaxPhysBoneColliders = 16;                  // VRChat Mobile PhysBone collider cap
            MaxPhysBoneCollisionChecks = 64;            // VRChat Mobile PhysBone collision check cap
            MaxContacts = 16;                           // VRChat Mobile Contact cap
            MaxConstraints = 150;                       // VRChat Mobile Constraint cap
            MaxConstraintDepth = 50;                    // VRChat Mobile Constraint depth cap
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
