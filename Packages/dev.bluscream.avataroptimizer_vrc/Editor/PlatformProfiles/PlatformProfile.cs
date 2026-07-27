using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using static Bluscream.EnumExtensions;

namespace Bluscream.VRCAvatarOptimizer
{
    public enum TargetPlatform
    {
        [Description("PC")] PC,
        [Description("Android")] Android,
        [Description("iOS")] iOS
    }

    public enum AvatarPerformanceRank
    {
        [Description("Excellent")] Excellent,
        [Description("Good")]      Good,
        [Description("Medium")]    Medium,
        [Description("Poor")]      Poor,
        [Description("Very Poor")] VeryPoor
    }

    public enum AssetPlacementLocation
    {
        SeparateFolder,
        SameFolderAsOriginal
    }

    public enum PhysBonePruningStrategy
    {
        Disabled = 0,
        DeepestFirst = 1,
        // 2 was ShallowestFirst (removed); explicit values keep stored EditorPrefs stable
        InteractiveChecklist = 3
    }

    /// <summary>
    /// Base abstract class for platform performance profiles defining resource and component limits
    /// according to official VRChat SDK Performance Rank specifications.
    /// </summary>
    [Serializable]
    public abstract class PlatformProfile
    {
        public abstract TargetPlatform Platform { get; }
        public abstract AvatarPerformanceRank Rank { get; }

        // Geometry & Mesh Limits
        public int MaxTriangles = int.MaxValue;
        public int MaxSkinnedMeshes = int.MaxValue;
        public int MaxMeshRenderers = int.MaxValue;
        public int MaxMaterialSlots = int.MaxValue;
        public int MaxBones = int.MaxValue;
        public int MaxAnimators = int.MaxValue;
        public Vector3 MaxBoundsSize = new Vector3(5f, 6f, 5f);

        // Texture & Memory Limits
        public long MaxTextureMemoryBytes = 40 * 1024 * 1024L; // 40 MB

        // PhysBone Limits
        public int MaxPhysBoneComponents = 8;
        public int MaxPhysBoneTransforms = 64;
        public int MaxPhysBoneColliders = 16;
        public int MaxPhysBoneCollisionChecks = 64;

        // Particle System Limits
        public int MaxParticleSystems = int.MaxValue;
        public int MaxActiveParticles = int.MaxValue;
        public int MaxMeshParticlePolyCount = int.MaxValue;
        public bool ParticleTrailsEnabledAllowed = true;
        public bool ParticleCollisionEnabledAllowed = true;

        // Renderers & Constraints
        public int MaxTrailRenderers = int.MaxValue;
        public int MaxLineRenderers = int.MaxValue;
        public int MaxRaycasts = int.MaxValue;
        public int MaxConstraints = int.MaxValue;
        public int MaxConstraintDepth = int.MaxValue;

        // Physics & Cloth
        public int MaxClothComponents = int.MaxValue;
        public int MaxClothVertices = int.MaxValue;
        public int MaxPhysicsColliders = int.MaxValue;
        public int MaxRigidbodies = int.MaxValue;

        // Lights & Audio
        public int MaxLights = int.MaxValue;
        public int MaxAudioSources = int.MaxValue;

        // Contact Limits
        public int MaxContacts = int.MaxValue;

        // Asset Bundle Size Limit
        public virtual long MaxAssetBundleSizeBytes => long.MaxValue;

        /// <summary>
        /// Reads the compressed avatar bundle size limit from the VRChat SDK
        /// (VRC.ValidationHelpers.GetAssetBundleSizeLimit) so the package follows SDK updates.
        /// Falls back to the given value when the SDK is unavailable.
        /// </summary>
        private static long? _sdkMobileBundleLimit;
        private static long? _sdkPcBundleLimit;

        protected static long GetSdkAssetBundleSizeLimit(bool isMobilePlatform, long fallbackBytes)
        {
            long? cached = isMobilePlatform ? _sdkMobileBundleLimit : _sdkPcBundleLimit;
            if (cached.HasValue) return cached.Value;

            long result = fallbackBytes;
            try
            {
                Type helpers = Type.GetType("VRC.ValidationHelpers, VRCSDKBase");
                Type contentType = Type.GetType("VRC.ContentType, VRCSDKBase");
                if (helpers != null && contentType != null)
                {
                    var method = helpers.GetMethod("GetAssetBundleSizeLimit");
                    if (method != null)
                    {
                        object avatar = Enum.Parse(contentType, "Avatar");
                        var args = method.GetParameters().Length == 3
                            ? new object[] { avatar, isMobilePlatform, true }
                            : new object[] { avatar, isMobilePlatform };
                        result = Convert.ToInt64(method.Invoke(null, args));
                    }
                }
            }
            catch { /* SDK not present or API changed — use fallback */ }

            if (isMobilePlatform) _sdkMobileBundleLimit = result;
            else _sdkPcBundleLimit = result;
            return result;
        }

        // Platform suffix for duplicated avatars/materials, e.g. " (Android) [Very Poor]"
        public virtual string PlatformSuffix => $" ({Platform.GetDescription()}) [{Rank.GetDescription()}]";

        // Component Whitelists & Blacklists — lazy-cached, override CreateBlacklist/CreateWhitelist per platform
        private HashSet<string> _blacklist;
        public HashSet<string> BlacklistedComponentNames => _blacklist ??= CreateBlacklist();
        protected virtual HashSet<string> CreateBlacklist()
            => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private HashSet<string> _whitelist;
        public HashSet<string> WhitelistedComponentNames => _whitelist ??= CreateWhitelist();
        protected virtual HashSet<string> CreateWhitelist()
            => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Performs custom, platform-specific component compatibility check.
        /// Returns true if component should be removed.
        /// </summary>
        public virtual bool ShouldRemoveComponentCustom(UnityEngine.Component comp)
        {
            return false;
        }

        /// <summary>
        /// Executes custom platform-specific optimization and conversion operations on the target avatar.
        /// </summary>
        public virtual void ExecutePlatformConversions(GameObject avatarRoot, Action<string> progressCallback = null)
        {
        }

        /// <summary>
        /// Validates platform-specific requirements and reports issues or warnings.
        /// The base implementation checks the avatar's combined render bounds against MaxBoundsSize.
        /// </summary>
        public virtual void ValidatePlatformRules(GameObject avatarRoot, ConversionSummary summary)
        {
            if (avatarRoot == null || summary == null) return;

            Renderer[] renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            Bounds combined = default;
            bool first = true;
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                if (first) { combined = r.bounds; first = false; }
                else combined.Encapsulate(r.bounds);
            }
            if (first) return;

            Vector3 size = combined.size;
            if (size.x > MaxBoundsSize.x || size.y > MaxBoundsSize.y || size.z > MaxBoundsSize.z)
            {
                summary.AddWarning(
                    $"Avatar bounds {size.x:F1}x{size.y:F1}x{size.z:F1}m exceed the {Rank} rank limit of {MaxBoundsSize.x}x{MaxBoundsSize.y}x{MaxBoundsSize.z}m.",
                    avatarRoot);
            }
        }

        public static PlatformProfile GetProfile(TargetPlatform platform, AvatarPerformanceRank rank)
        {
            if (platform == TargetPlatform.PC)
            {
                switch (rank)
                {
                    case AvatarPerformanceRank.Excellent: return new PlatformProfile_PC_Excellent();
                    case AvatarPerformanceRank.Good: return new PlatformProfile_PC_Good();
                    case AvatarPerformanceRank.Medium: return new PlatformProfile_PC_Medium();
                    case AvatarPerformanceRank.Poor: return new PlatformProfile_PC_Poor();
                    case AvatarPerformanceRank.VeryPoor:
                    default: return new PlatformProfile_PC_VeryPoor();
                }
            }
            else if (platform == TargetPlatform.iOS)
            {
                switch (rank)
                {
                    case AvatarPerformanceRank.Excellent: return new PlatformProfile_iOS_Excellent();
                    case AvatarPerformanceRank.Good: return new PlatformProfile_iOS_Good();
                    case AvatarPerformanceRank.Medium: return new PlatformProfile_iOS_Medium();
                    case AvatarPerformanceRank.Poor: return new PlatformProfile_iOS_Poor();
                    case AvatarPerformanceRank.VeryPoor:
                    default: return new PlatformProfile_iOS_VeryPoor();
                }
            }
            else
            {
                switch (rank)
                {
                    case AvatarPerformanceRank.Excellent: return new PlatformProfile_Android_Excellent();
                    case AvatarPerformanceRank.Good: return new PlatformProfile_Android_Good();
                    case AvatarPerformanceRank.Medium: return new PlatformProfile_Android_Medium();
                    case AvatarPerformanceRank.Poor: return new PlatformProfile_Android_Poor();
                    case AvatarPerformanceRank.VeryPoor:
                    default: return new PlatformProfile_Android_VeryPoor();
                }
            }
        }
    }
}
