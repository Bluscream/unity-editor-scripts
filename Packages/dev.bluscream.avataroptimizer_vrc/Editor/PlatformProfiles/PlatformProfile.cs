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
    /// Extends ProfileLimitData so all limit fields are defined once and shared with the JSON config system.
    /// </summary>
    [Serializable]
    public abstract class PlatformProfile : ProfileLimitData
    {
        public abstract TargetPlatform Platform { get; }
        public abstract AvatarPerformanceRank Rank { get; }

        // MaxBoundsSize is not in ProfileLimitData because it's a Vector3 (no clean unlimited sentinel)
        public Vector3 MaxBoundsSize = new Vector3(5f, 6f, 5f);

        // NOTE: MaxAssetBundleSizeBytes is inherited as a long field from ProfileLimitData.
        //       Platform subclasses (Android, PC) set it in their constructors via GetSdkAssetBundleSizeLimit()
        //       so SDK updates are picked up automatically at runtime.

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

        // Component Whitelists & Blacklists — lazy-cached HashSets built from ProfileLimitData.ComponentBlacklist/Whitelist
        // plus any extra runtime type checks added by virtual CreateBlacklist/CreateWhitelist overrides.
        private HashSet<string> _blacklistSet;
        public HashSet<string> BlacklistedComponentNames => _blacklistSet ??= BuildComponentSet(ComponentBlacklist, CreateBlacklist());
        protected virtual HashSet<string> CreateBlacklist()
            => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private HashSet<string> _whitelistSet;
        public HashSet<string> WhitelistedComponentNames => _whitelistSet ??= BuildComponentSet(ComponentWhitelist, CreateWhitelist());
        protected virtual HashSet<string> CreateWhitelist()
            => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static HashSet<string> BuildComponentSet(List<string> configList, HashSet<string> runtimeSet)
        {
            if (configList?.Count > 0)
                foreach (var n in configList) runtimeSet.Add(n);
            return runtimeSet;
        }

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

        private static PlatformProfile ApplyConfigLimits(PlatformProfile profile)
        {
            if (profile == null || OptimizerConfig.ActiveConfig?.ProfileDict == null) return profile;

            if (OptimizerConfig.ActiveConfig.ProfileDict.TryGetValue(profile.Platform.ToString(), out var ranks) &&
                ranks.TryGetValue(profile.Rank.ToString(), out ProfileLimitData data))
            {
                // MergeWith uses profile's hardcoded defaults as base; data non-unlimited values override.
                // ApplyFrom writes the merged result back in-place onto the profile.
                profile.ApplyFrom(profile.MergeWith(data));
                profile._blacklistSet = null; // invalidate cached set so it rebuilds with new ComponentBlacklist
                profile._whitelistSet = null;
            }

            return profile;
        }

        public static PlatformProfile GetProfile(TargetPlatform platform, AvatarPerformanceRank rank)
        {
            PlatformProfile profile = null;
            if (platform == TargetPlatform.PC)
            {
                switch (rank)
                {
                    case AvatarPerformanceRank.Excellent: profile = new PlatformProfile_PC_Excellent(); break;
                    case AvatarPerformanceRank.Good: profile = new PlatformProfile_PC_Good(); break;
                    case AvatarPerformanceRank.Medium: profile = new PlatformProfile_PC_Medium(); break;
                    case AvatarPerformanceRank.Poor: profile = new PlatformProfile_PC_Poor(); break;
                    case AvatarPerformanceRank.VeryPoor:
                    default: profile = new PlatformProfile_PC_VeryPoor(); break;
                }
            }
            else if (platform == TargetPlatform.iOS)
            {
                switch (rank)
                {
                    case AvatarPerformanceRank.Excellent: profile = new PlatformProfile_iOS_Excellent(); break;
                    case AvatarPerformanceRank.Good: profile = new PlatformProfile_iOS_Good(); break;
                    case AvatarPerformanceRank.Medium: profile = new PlatformProfile_iOS_Medium(); break;
                    case AvatarPerformanceRank.Poor: profile = new PlatformProfile_iOS_Poor(); break;
                    case AvatarPerformanceRank.VeryPoor:
                    default: profile = new PlatformProfile_iOS_VeryPoor(); break;
                }
            }
            else
            {
                switch (rank)
                {
                    case AvatarPerformanceRank.Excellent: profile = new PlatformProfile_Android_Excellent(); break;
                    case AvatarPerformanceRank.Good: profile = new PlatformProfile_Android_Good(); break;
                    case AvatarPerformanceRank.Medium: profile = new PlatformProfile_Android_Medium(); break;
                    case AvatarPerformanceRank.Poor: profile = new PlatformProfile_Android_Poor(); break;
                    case AvatarPerformanceRank.VeryPoor:
                    default: profile = new PlatformProfile_Android_VeryPoor(); break;
                }
            }

            return ApplyConfigLimits(profile);
        }
    }
}
