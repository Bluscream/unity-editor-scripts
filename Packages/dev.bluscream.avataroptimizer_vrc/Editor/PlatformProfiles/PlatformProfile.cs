using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using Bluscream.VRC;
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
        /// Reads the compressed avatar bundle size limit from the VRChat SDK via VRCSDKReflectionHelper.
        /// Falls back to the given value when the SDK is unavailable.
        /// </summary>
        protected static long GetSdkAssetBundleSizeLimit(bool isMobilePlatform, long fallbackBytes)
            => VRCSDKReflectionHelper.TryGetAssetBundleSizeLimit(isMobilePlatform, out long limit) ? limit : fallbackBytes;

        // Platform suffix for duplicated avatars/materials, e.g. " (Android) [Very Poor]"
        public virtual string PlatformSuffix => $" ({Platform.GetDescription()}) [{Rank.GetDescription()}]";

        /// <summary>
        /// Formats all active performance limits into a multi-line human-readable summary string.
        /// </summary>
        public override string ToString()
        {
            var lines = new List<string>();

            string FormatInt(int val) => val == int.MaxValue ? "Unlimited" : val.ToString("N0");
            string FormatMB(long val) => val == long.MaxValue ? "Unlimited" : $"{val / (1024.0 * 1024.0):F1} MB";

            lines.Add($"• Max Triangles: {FormatInt(MaxTriangles)}");
            lines.Add($"• Max Asset Bundle Size: {FormatMB(MaxAssetBundleSizeBytes)}");
            lines.Add($"• Max Texture Memory: {FormatMB(MaxTextureMemoryBytes)}");
            lines.Add($"• Max Skinned Meshes: {FormatInt(MaxSkinnedMeshes)} | Static Mesh Renderers: {FormatInt(MaxMeshRenderers)}");
            lines.Add($"• Max Material Slots: {FormatInt(MaxMaterialSlots)} | Bones: {FormatInt(MaxBones)} | Animators: {FormatInt(MaxAnimators)}");
            lines.Add($"• PhysBones: {FormatInt(MaxPhysBoneComponents)} components ({FormatInt(MaxPhysBoneTransforms)} transforms, {FormatInt(MaxPhysBoneColliders)} colliders, {FormatInt(MaxPhysBoneCollisionChecks)} checks)");
            lines.Add($"• Contacts: {FormatInt(MaxContacts)} | Constraints: {FormatInt(MaxConstraints)} (depth {FormatInt(MaxConstraintDepth)})");

            if (MaxParticleSystems != int.MaxValue || MaxActiveParticles != int.MaxValue || MaxMeshParticlePolyCount != int.MaxValue || !ParticleTrailsEnabledAllowed || !ParticleCollisionEnabledAllowed)
            {
                var pParts = new List<string>();
                if (MaxParticleSystems != int.MaxValue) pParts.Add($"{FormatInt(MaxParticleSystems)} systems");
                if (MaxActiveParticles != int.MaxValue) pParts.Add($"{FormatInt(MaxActiveParticles)} active particles");
                if (MaxMeshParticlePolyCount != int.MaxValue) pParts.Add($"{FormatInt(MaxMeshParticlePolyCount)} mesh polys");
                if (!ParticleTrailsEnabledAllowed) pParts.Add("trails forbidden");
                if (!ParticleCollisionEnabledAllowed) pParts.Add("collision forbidden");
                lines.Add($"• Particles: {string.Join(", ", pParts)}");
            }

            if (MaxTrailRenderers != int.MaxValue || MaxLineRenderers != int.MaxValue)
                lines.Add($"• Trail/Line Renderers: {FormatInt(MaxTrailRenderers)} trails, {FormatInt(MaxLineRenderers)} lines");

            if (MaxClothComponents != int.MaxValue || MaxClothVertices != int.MaxValue || MaxPhysicsColliders != int.MaxValue || MaxRigidbodies != int.MaxValue)
                lines.Add($"• Physics & Cloth: {FormatInt(MaxPhysicsColliders)} colliders, {FormatInt(MaxRigidbodies)} rigidbodies, {FormatInt(MaxClothComponents)} cloth ({FormatInt(MaxClothVertices)} verts)");

            if (MaxLights != int.MaxValue || MaxAudioSources != int.MaxValue)
                lines.Add($"• Lights & Audio: {FormatInt(MaxLights)} lights, {FormatInt(MaxAudioSources)} audio sources");

            if (MaxRaycasts != int.MaxValue)
                lines.Add($"• Max Raycasts: {FormatInt(MaxRaycasts)}");

            if (MaxBoundsSize != Vector3.zero)
                lines.Add($"• Max Avatar Bounds: {MaxBoundsSize.x}x{MaxBoundsSize.y}x{MaxBoundsSize.z}m");

            if (WhitelistedComponentNames != null && WhitelistedComponentNames.Count > 0)
                lines.Add($"• Whitelisted Components ({WhitelistedComponentNames.Count}): {string.Join(", ", WhitelistedComponentNames)}");

            if (BlacklistedComponentNames != null && BlacklistedComponentNames.Count > 0)
                lines.Add($"• Blacklisted Components ({BlacklistedComponentNames.Count}): {string.Join(", ", BlacklistedComponentNames)}");

            return string.Join("\n", lines);
        }

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

        /// <summary>
        /// Attempts to extract performance rank limits directly from the VRChat SDK at runtime via Reflection.
        /// Returns true if limits were successfully fetched from the VRChat SDK.
        /// </summary>
        public static bool TryGetLimitsFromSDK(TargetPlatform platform, AvatarPerformanceRank rank, out ProfileLimitData sdkLimits)
        {
            sdkLimits = null;
            string sdkPlatformName = platform == TargetPlatform.PC ? "PC" : "Android";
            if (!VRCSDKReflectionHelper.TryGetPerformanceRatingStats(sdkPlatformName, rank.ToString(), out object ratingStatsObj))
                return false;

            sdkLimits = new ProfileLimitData();
            bool isMobile = platform == TargetPlatform.Android || platform == TargetPlatform.iOS;
            sdkLimits.MaxAssetBundleSizeBytes = GetSdkAssetBundleSizeLimit(isMobile, isMobile ? 10485760L : 209715200L);

            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "polyCount", out int poly) || VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "triangleCount", out poly))
                sdkLimits.MaxTriangles = poly;

            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "skinnedMeshCount", out int sm)) sdkLimits.MaxSkinnedMeshes = sm;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "meshRendererCount", out int mr)) sdkLimits.MaxMeshRenderers = mr;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "materialCount", out int mat)) sdkLimits.MaxMaterialSlots = mat;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "boneCount", out int bone)) sdkLimits.MaxBones = bone;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "animatorCount", out int anim)) sdkLimits.MaxAnimators = anim;

            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "physBoneComponentCount", out int pbc)) sdkLimits.MaxPhysBoneComponents = pbc;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "physBoneTransformCount", out int pbt)) sdkLimits.MaxPhysBoneTransforms = pbt;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "physBoneColliderCount", out int pbcol)) sdkLimits.MaxPhysBoneColliders = pbcol;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "physBoneCollisionCheckCount", out int pbchk)) sdkLimits.MaxPhysBoneCollisionChecks = pbchk;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "contactCount", out int cnt)) sdkLimits.MaxContacts = cnt;

            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "particleSystemCount", out int ps)) sdkLimits.MaxParticleSystems = ps;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "particleActiveCount", out int pa)) sdkLimits.MaxActiveParticles = pa;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "particlePolyCount", out int pp)) sdkLimits.MaxMeshParticlePolyCount = pp;

            if (VRCSDKReflectionHelper.TryGetBoolStat(ratingStatsObj, "particleTrailsEnabled", out bool pt)) sdkLimits.ParticleTrailsEnabledAllowed = pt;
            if (VRCSDKReflectionHelper.TryGetBoolStat(ratingStatsObj, "particleCollisionEnabled", out bool pc)) sdkLimits.ParticleCollisionEnabledAllowed = pc;

            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "trailRendererCount", out int tr)) sdkLimits.MaxTrailRenderers = tr;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "lineRendererCount", out int lr)) sdkLimits.MaxLineRenderers = lr;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "constraintCount", out int cc)) sdkLimits.MaxConstraints = cc;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "constraintDepth", out int cd)) sdkLimits.MaxConstraintDepth = cd;

            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "clothCount", out int cloth)) sdkLimits.MaxClothComponents = cloth;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "clothVertexCount", out int clothv)) sdkLimits.MaxClothVertices = clothv;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "physicsColliderCount", out int pcol)) sdkLimits.MaxPhysicsColliders = pcol;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "rigidbodyCount", out int rb)) sdkLimits.MaxRigidbodies = rb;

            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "lightCount", out int light)) sdkLimits.MaxLights = light;
            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "audioSourceCount", out int audio)) sdkLimits.MaxAudioSources = audio;

            if (VRCSDKReflectionHelper.TryGetLongStat(ratingStatsObj, "textureMemoryBytes", out long vram) || VRCSDKReflectionHelper.TryGetLongStat(ratingStatsObj, "textureMemory", out vram))
                sdkLimits.MaxTextureMemoryBytes = vram;

            if (VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "raycastCount", out int ray) || VRCSDKReflectionHelper.TryGetIntStat(ratingStatsObj, "maxRaycasts", out ray))
                sdkLimits.MaxRaycasts = ray;

            if (isMobile && VRCSDKReflectionHelper.TryGetForbiddenComponents(out var blackList) && blackList.Count > 0)
                sdkLimits.ComponentBlacklist = blackList;

            return true;
        }

        private static void LogSdkOverrides(PlatformProfile profile, ProfileLimitData sdkLimits)
        {
            if (profile == null || sdkLimits == null) return;

            var diffs = new List<string>();

            void CheckInt(string name, int currentVal, int sdkVal)
            {
                if (sdkVal != int.MaxValue && sdkVal != currentVal)
                {
                    string curStr = currentVal == int.MaxValue ? "Unlimited" : currentVal.ToString();
                    string sdkStr = sdkVal.ToString();
                    diffs.Add($"{name}: {curStr} -> {sdkStr}");
                }
            }

            void CheckLong(string name, long currentVal, long sdkVal)
            {
                if (sdkVal != long.MaxValue && sdkVal != currentVal)
                {
                    string curStr = currentVal == long.MaxValue ? "Unlimited" : currentVal.ToString();
                    string sdkStr = sdkVal.ToString();
                    diffs.Add($"{name}: {curStr} -> {sdkStr}");
                }
            }

            void CheckBool(string name, bool currentVal, bool sdkVal)
            {
                if (sdkVal != currentVal)
                {
                    diffs.Add($"{name}: {currentVal} -> {sdkVal}");
                }
            }

            CheckInt("MaxTriangles", profile.MaxTriangles, sdkLimits.MaxTriangles);
            CheckInt("MaxSkinnedMeshes", profile.MaxSkinnedMeshes, sdkLimits.MaxSkinnedMeshes);
            CheckInt("MaxMeshRenderers", profile.MaxMeshRenderers, sdkLimits.MaxMeshRenderers);
            CheckInt("MaxMaterialSlots", profile.MaxMaterialSlots, sdkLimits.MaxMaterialSlots);
            CheckInt("MaxBones", profile.MaxBones, sdkLimits.MaxBones);
            CheckInt("MaxAnimators", profile.MaxAnimators, sdkLimits.MaxAnimators);

            CheckLong("MaxTextureMemoryBytes", profile.MaxTextureMemoryBytes, sdkLimits.MaxTextureMemoryBytes);
            CheckLong("MaxAssetBundleSizeBytes", profile.MaxAssetBundleSizeBytes, sdkLimits.MaxAssetBundleSizeBytes);

            CheckInt("MaxPhysBoneComponents", profile.MaxPhysBoneComponents, sdkLimits.MaxPhysBoneComponents);
            CheckInt("MaxPhysBoneTransforms", profile.MaxPhysBoneTransforms, sdkLimits.MaxPhysBoneTransforms);
            CheckInt("MaxPhysBoneColliders", profile.MaxPhysBoneColliders, sdkLimits.MaxPhysBoneColliders);
            CheckInt("MaxPhysBoneCollisionChecks", profile.MaxPhysBoneCollisionChecks, sdkLimits.MaxPhysBoneCollisionChecks);
            CheckInt("MaxContacts", profile.MaxContacts, sdkLimits.MaxContacts);

            CheckInt("MaxParticleSystems", profile.MaxParticleSystems, sdkLimits.MaxParticleSystems);
            CheckInt("MaxActiveParticles", profile.MaxActiveParticles, sdkLimits.MaxActiveParticles);
            CheckInt("MaxMeshParticlePolyCount", profile.MaxMeshParticlePolyCount, sdkLimits.MaxMeshParticlePolyCount);
            CheckBool("ParticleTrailsEnabledAllowed", profile.ParticleTrailsEnabledAllowed, sdkLimits.ParticleTrailsEnabledAllowed);
            CheckBool("ParticleCollisionEnabledAllowed", profile.ParticleCollisionEnabledAllowed, sdkLimits.ParticleCollisionEnabledAllowed);

            CheckInt("MaxTrailRenderers", profile.MaxTrailRenderers, sdkLimits.MaxTrailRenderers);
            CheckInt("MaxLineRenderers", profile.MaxLineRenderers, sdkLimits.MaxLineRenderers);
            CheckInt("MaxConstraints", profile.MaxConstraints, sdkLimits.MaxConstraints);
            CheckInt("MaxConstraintDepth", profile.MaxConstraintDepth, sdkLimits.MaxConstraintDepth);

            CheckInt("MaxClothComponents", profile.MaxClothComponents, sdkLimits.MaxClothComponents);
            CheckInt("MaxClothVertices", profile.MaxClothVertices, sdkLimits.MaxClothVertices);
            CheckInt("MaxPhysicsColliders", profile.MaxPhysicsColliders, sdkLimits.MaxPhysicsColliders);
            CheckInt("MaxRigidbodies", profile.MaxRigidbodies, sdkLimits.MaxRigidbodies);

            CheckInt("MaxLights", profile.MaxLights, sdkLimits.MaxLights);
            CheckInt("MaxAudioSources", profile.MaxAudioSources, sdkLimits.MaxAudioSources);

            if (diffs.Count > 0)
            {
                string msg = $"[VRCAvatarOptimizer] Overrode limits from VRChat SDK for {profile.Platform} [{profile.Rank}]:\n" + string.Join("\n", diffs);
                Debug.Log(msg);
            }
        }

        private static PlatformProfile ApplyConfigLimits(PlatformProfile profile)
        {
            if (profile == null) return profile;

            // Automatically query and apply live VRChat SDK limits if SDK is present
            if (TryGetLimitsFromSDK(profile.Platform, profile.Rank, out ProfileLimitData sdkLimits))
            {
                LogSdkOverrides(profile, sdkLimits);
                profile.ApplyFrom(profile.MergeWith(sdkLimits));
            }

            if (OptimizerConfig.ActiveConfig?.ProfileDict != null &&
                OptimizerConfig.ActiveConfig.ProfileDict.TryGetValue(profile.Platform.ToString(), out var ranks) &&
                ranks.TryGetValue(profile.Rank.ToString(), out ProfileLimitData data))
            {
                long originalBundleLimit = profile.MaxAssetBundleSizeBytes;
                // MergeWith uses profile's hardcoded defaults as base; data non-unlimited values override.
                // ApplyFrom writes the merged result back in-place onto the profile.
                profile.ApplyFrom(profile.MergeWith(data));

                // If config specified a specific MaxAssetBundleSizeBytes (not long.MaxValue), use it over the SDK lookup
                if (data.MaxAssetBundleSizeBytes != long.MaxValue)
                {
                    profile.MaxAssetBundleSizeBytes = data.MaxAssetBundleSizeBytes;
                }
                else
                {
                    profile.MaxAssetBundleSizeBytes = originalBundleLimit;
                }

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
