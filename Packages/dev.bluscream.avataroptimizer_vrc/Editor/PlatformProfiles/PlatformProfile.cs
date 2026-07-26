using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using UnityEngine;

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
        Disabled,
        DeepestFirst,
        ShallowestFirst,
        InteractiveChecklist
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
        public long MaxTextureMemoryBytes = 40 * 1024 * 1024L;

        // PhysBone Limits
        public int MaxPhysBoneComponents = 8;
        public int MaxPhysBoneTransforms = 64;
        public int MaxPhysBoneColliders = 16;
        public int MaxPhysBoneCollisionChecks = 64;

        // Particle System Limits
        public int MaxMeshParticlePolyCount = int.MaxValue;
        public int MaxParticleSystems = int.MaxValue;

        // Lights & Audio
        public int MaxLights = int.MaxValue;
        public int MaxAudioSources = int.MaxValue;

        // Contact Limits
        public virtual int MaxContacts => int.MaxValue;

        // Asset Bundle Size Limit
        public virtual long MaxAssetBundleSizeBytes => long.MaxValue;

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
        public virtual bool ShouldRemoveComponentCustom(Component comp)
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
        /// </summary>
        public virtual void ValidatePlatformRules(GameObject avatarRoot, ConversionSummary summary)
        {
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

    /// <summary>
    /// Extension methods for reading [Description] attributes from enum values.
    /// </summary>
    public static class EnumExtensions
    {
        /// <summary>
        /// Returns the [Description] attribute string for an enum value,
        /// or falls back to .ToString() if none is set.
        /// </summary>
        public static string GetDescription<T>(this T value) where T : Enum
        {
            FieldInfo field = typeof(T).GetField(value.ToString());
            if (field == null) return value.ToString();
            DescriptionAttribute attr = field.GetCustomAttribute<DescriptionAttribute>();
            return attr != null ? attr.Description : value.ToString();
        }
    }
}
