using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public enum RuleMatchType
    {
        Exact,
        StartsWith,
        EndsWith,
        Contains,
        Regex
    }

    [Serializable]
    public class ShaderMappingRule
    {
        public int priority = 100; // Lower = evaluated first;
        public string description;
        public string matchType = "Contains"; // Exact, StartsWith, EndsWith, Contains, Regex
        public string pattern;
        public string targetShader;
        public bool caseSensitive = false;
        public List<string> requiredProperties = new List<string>();
    }

    [Serializable]
    public class ShaderMappingData
    {
        public List<ShaderMappingRule> rules = new List<ShaderMappingRule>();
    }

    [Serializable]
    public class ProfileLimitData
    {
        // Geometry & Mesh
        public int MaxTriangles = int.MaxValue;
        public int MaxSkinnedMeshes = int.MaxValue;
        public int MaxMeshRenderers = int.MaxValue;
        public int MaxMaterialSlots = int.MaxValue;
        public int MaxBones = int.MaxValue;
        public int MaxAnimators = int.MaxValue;

        // Texture & Memory
        public long MaxTextureMemoryBytes = long.MaxValue; // 40 MB default
        public long MaxAssetBundleSizeBytes = long.MaxValue;

        // PhysBone
        public int MaxPhysBoneComponents = int.MaxValue;
        public int MaxPhysBoneTransforms = int.MaxValue;
        public int MaxPhysBoneColliders = int.MaxValue;
        public int MaxPhysBoneCollisionChecks = int.MaxValue;
        public int MaxContacts = int.MaxValue;

        // Particle Systems
        public int MaxParticleSystems = int.MaxValue;
        public int MaxActiveParticles = int.MaxValue;
        public int MaxMeshParticlePolyCount = int.MaxValue;
        public bool ParticleTrailsEnabledAllowed = true;
        public bool ParticleCollisionEnabledAllowed = true;

        // Renderers, Constraints & Raycasts
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

        // Component filtering — names are matched case-insensitively against component type names.
        // Blacklist = forcibly removed; Whitelist = the only ones allowed (empty = allow all).
        public List<string> ComponentBlacklist = new List<string>();
        public List<string> ComponentWhitelist = new List<string>();

        /// <summary>Merge another ProfileLimitData on top of this one, overriding any fields that are not at their default unlimited value.</summary>
        public ProfileLimitData MergeWith(ProfileLimitData overlay)
        {
            if (overlay == null) return this;
            var r = new ProfileLimitData();
            r.MaxTriangles              = overlay.MaxTriangles              != int.MaxValue  ? overlay.MaxTriangles              : MaxTriangles;
            r.MaxSkinnedMeshes          = overlay.MaxSkinnedMeshes          != int.MaxValue  ? overlay.MaxSkinnedMeshes          : MaxSkinnedMeshes;
            r.MaxMeshRenderers          = overlay.MaxMeshRenderers          != int.MaxValue  ? overlay.MaxMeshRenderers          : MaxMeshRenderers;
            r.MaxMaterialSlots          = overlay.MaxMaterialSlots          != int.MaxValue  ? overlay.MaxMaterialSlots          : MaxMaterialSlots;
            r.MaxBones                  = overlay.MaxBones                  != int.MaxValue  ? overlay.MaxBones                  : MaxBones;
            r.MaxAnimators              = overlay.MaxAnimators              != int.MaxValue  ? overlay.MaxAnimators              : MaxAnimators;
            r.MaxTextureMemoryBytes     = overlay.MaxTextureMemoryBytes     != long.MaxValue ? overlay.MaxTextureMemoryBytes     : MaxTextureMemoryBytes;
            r.MaxAssetBundleSizeBytes   = overlay.MaxAssetBundleSizeBytes   != long.MaxValue ? overlay.MaxAssetBundleSizeBytes   : MaxAssetBundleSizeBytes;
            r.MaxPhysBoneComponents     = overlay.MaxPhysBoneComponents     != int.MaxValue  ? overlay.MaxPhysBoneComponents     : MaxPhysBoneComponents;
            r.MaxPhysBoneTransforms     = overlay.MaxPhysBoneTransforms     != int.MaxValue  ? overlay.MaxPhysBoneTransforms     : MaxPhysBoneTransforms;
            r.MaxPhysBoneColliders      = overlay.MaxPhysBoneColliders      != int.MaxValue  ? overlay.MaxPhysBoneColliders      : MaxPhysBoneColliders;
            r.MaxPhysBoneCollisionChecks= overlay.MaxPhysBoneCollisionChecks!= int.MaxValue  ? overlay.MaxPhysBoneCollisionChecks: MaxPhysBoneCollisionChecks;
            r.MaxContacts               = overlay.MaxContacts               != int.MaxValue  ? overlay.MaxContacts               : MaxContacts;
            r.MaxParticleSystems        = overlay.MaxParticleSystems        != int.MaxValue  ? overlay.MaxParticleSystems        : MaxParticleSystems;
            r.MaxActiveParticles        = overlay.MaxActiveParticles        != int.MaxValue  ? overlay.MaxActiveParticles        : MaxActiveParticles;
            r.MaxMeshParticlePolyCount  = overlay.MaxMeshParticlePolyCount  != int.MaxValue  ? overlay.MaxMeshParticlePolyCount  : MaxMeshParticlePolyCount;
            r.ParticleTrailsEnabledAllowed    = overlay.ParticleTrailsEnabledAllowed;
            r.ParticleCollisionEnabledAllowed = overlay.ParticleCollisionEnabledAllowed;
            r.MaxTrailRenderers         = overlay.MaxTrailRenderers         != int.MaxValue  ? overlay.MaxTrailRenderers         : MaxTrailRenderers;
            r.MaxLineRenderers          = overlay.MaxLineRenderers          != int.MaxValue  ? overlay.MaxLineRenderers          : MaxLineRenderers;
            r.MaxConstraints            = overlay.MaxConstraints            != int.MaxValue  ? overlay.MaxConstraints            : MaxConstraints;
            r.MaxConstraintDepth        = overlay.MaxConstraintDepth        != int.MaxValue  ? overlay.MaxConstraintDepth        : MaxConstraintDepth;
            r.MaxClothComponents        = overlay.MaxClothComponents        != int.MaxValue  ? overlay.MaxClothComponents        : MaxClothComponents;
            r.MaxClothVertices          = overlay.MaxClothVertices          != int.MaxValue  ? overlay.MaxClothVertices          : MaxClothVertices;
            r.MaxPhysicsColliders       = overlay.MaxPhysicsColliders       != int.MaxValue  ? overlay.MaxPhysicsColliders       : MaxPhysicsColliders;
            r.MaxRigidbodies            = overlay.MaxRigidbodies            != int.MaxValue  ? overlay.MaxRigidbodies            : MaxRigidbodies;
            r.MaxLights                 = overlay.MaxLights                 != int.MaxValue  ? overlay.MaxLights                 : MaxLights;
            r.MaxAudioSources           = overlay.MaxAudioSources           != int.MaxValue  ? overlay.MaxAudioSources           : MaxAudioSources;
            r.MaxRaycasts               = overlay.MaxRaycasts               != int.MaxValue  ? overlay.MaxRaycasts               : MaxRaycasts;
            // Union blacklists, prefer overlay whitelist if non-empty
            r.ComponentBlacklist = overlay.ComponentBlacklist?.Count > 0
                ? new List<string>(ComponentBlacklist ?? new List<string>()).Union(overlay.ComponentBlacklist, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>(ComponentBlacklist ?? new List<string>());
            r.ComponentWhitelist = overlay.ComponentWhitelist?.Count > 0
                ? new List<string>(overlay.ComponentWhitelist)
                : new List<string>(ComponentWhitelist ?? new List<string>());
            return r;
        }

        /// <summary>
        /// Copies EVERY limit field from <paramref name="overlay"/> onto this instance in-place —
        /// including fields still at their int.MaxValue/long.MaxValue "unlimited" default. It does not
        /// merge, so passing a sparse overlay here will wipe existing limits to unlimited.
        /// <para>
        /// Always feed it a <see cref="MergeWith"/> result, which is what resolves the sentinels:
        /// <c>profile.ApplyFrom(profile.MergeWith(overlay))</c>. That is the only pattern
        /// PlatformProfile.ApplyConfigLimits uses.
        /// </para>
        /// Component blacklists/whitelists are the exception: they are only touched when the overlay
        /// actually provides entries (blacklists union, whitelist replaces).
        /// </summary>
        public void ApplyFrom(ProfileLimitData overlay)
        {
            if (overlay == null) return;
            MaxTriangles               = overlay.MaxTriangles;
            MaxSkinnedMeshes           = overlay.MaxSkinnedMeshes;
            MaxMeshRenderers           = overlay.MaxMeshRenderers;
            MaxMaterialSlots           = overlay.MaxMaterialSlots;
            MaxBones                   = overlay.MaxBones;
            MaxAnimators               = overlay.MaxAnimators;
            MaxTextureMemoryBytes      = overlay.MaxTextureMemoryBytes;
            MaxAssetBundleSizeBytes    = overlay.MaxAssetBundleSizeBytes;
            MaxPhysBoneComponents      = overlay.MaxPhysBoneComponents;
            MaxPhysBoneTransforms      = overlay.MaxPhysBoneTransforms;
            MaxPhysBoneColliders       = overlay.MaxPhysBoneColliders;
            MaxPhysBoneCollisionChecks = overlay.MaxPhysBoneCollisionChecks;
            MaxContacts                = overlay.MaxContacts;
            MaxParticleSystems         = overlay.MaxParticleSystems;
            MaxActiveParticles         = overlay.MaxActiveParticles;
            MaxMeshParticlePolyCount   = overlay.MaxMeshParticlePolyCount;
            ParticleTrailsEnabledAllowed    = overlay.ParticleTrailsEnabledAllowed;
            ParticleCollisionEnabledAllowed = overlay.ParticleCollisionEnabledAllowed;
            MaxTrailRenderers          = overlay.MaxTrailRenderers;
            MaxLineRenderers           = overlay.MaxLineRenderers;
            MaxRaycasts                = overlay.MaxRaycasts;
            MaxConstraints             = overlay.MaxConstraints;
            MaxConstraintDepth         = overlay.MaxConstraintDepth;
            MaxClothComponents         = overlay.MaxClothComponents;
            MaxClothVertices           = overlay.MaxClothVertices;
            MaxPhysicsColliders        = overlay.MaxPhysicsColliders;
            MaxRigidbodies             = overlay.MaxRigidbodies;
            MaxLights                  = overlay.MaxLights;
            MaxAudioSources            = overlay.MaxAudioSources;
            if (overlay.ComponentBlacklist?.Count > 0)
                ComponentBlacklist = new List<string>(ComponentBlacklist ?? new List<string>()).Union(overlay.ComponentBlacklist, StringComparer.OrdinalIgnoreCase).ToList();
            if (overlay.ComponentWhitelist?.Count > 0)
                ComponentWhitelist = new List<string>(overlay.ComponentWhitelist);
        }
    }

    [Serializable]
    public class RankProfileData
    {
        public string name;
        public ProfileLimitData limits = new ProfileLimitData();
    }

    [Serializable]
    public class PlatformProfileData
    {
        public string name;
        /// <summary>Base limits for this platform applied to all ranks. Rank-specific limits override these.</summary>
        public ProfileLimitData limits = new ProfileLimitData();
        public List<RankProfileData> ranks = new List<RankProfileData>();
    }

    [Serializable]
    public class OptimizerConfigData
    {
        public string version = "1.0.0";
        public ShaderMappingData shaderMapping = new ShaderMappingData();
        public List<PlatformProfileData> platformProfiles = new List<PlatformProfileData>();

        // Fast lookup dictionaries populated automatically post-deserialization
        [NonSerialized]
        public Dictionary<string, string> LookupDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        [NonSerialized]
        public Dictionary<string, Dictionary<string, ProfileLimitData>> ProfileDict = new Dictionary<string, Dictionary<string, ProfileLimitData>>(StringComparer.OrdinalIgnoreCase);

        public void BuildLookupDictionaries()
        {
            // Sort rules by priority ascending (lower priority number = evaluated first)
            shaderMapping?.rules?.Sort((a, b) => (a?.priority ?? int.MaxValue).CompareTo(b?.priority ?? int.MaxValue));

            LookupDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (shaderMapping?.rules != null)
            {
                foreach (var rule in shaderMapping.rules)
                {
                    if (rule == null) continue;
                    if (string.Equals(rule.matchType, "Exact", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(rule.pattern) &&
                        !string.IsNullOrWhiteSpace(rule.targetShader) &&
                        (rule.requiredProperties == null || rule.requiredProperties.Count == 0))
                    {
                        LookupDict[rule.pattern] = rule.targetShader;
                    }
                }
            }

            ProfileDict = new Dictionary<string, Dictionary<string, ProfileLimitData>>(StringComparer.OrdinalIgnoreCase);
            if (platformProfiles != null)
            {
                foreach (var platData in platformProfiles)
                {
                    if (string.IsNullOrWhiteSpace(platData.name) || platData.ranks == null) continue;
                    var baseLimits = platData.limits ?? new ProfileLimitData();
                    var rankDict = new Dictionary<string, ProfileLimitData>(StringComparer.OrdinalIgnoreCase);
                    foreach (var rankData in platData.ranks)
                    {
                        if (!string.IsNullOrWhiteSpace(rankData.name))
                        {
                            // Rank limits override base platform limits; missing fields fall through to baseLimits
                            rankDict[rankData.name] = baseLimits.MergeWith(rankData.limits);
                        }
                    }
                    ProfileDict[platData.name] = rankDict;
                }
            }
        }
    }

    [InitializeOnLoad]
    public static class OptimizerConfig
    {
        private const string REMOTE_URL = "https://raw.githubusercontent.com/Bluscream/unity-editor-scripts/main/Packages/dev.bluscream.avataroptimizer_vrc/config.json";
        private const string LOCAL_PATH = "Packages/dev.bluscream.avataroptimizer_vrc/config.json";

        public static OptimizerConfigData ActiveConfig { get; private set; }

        static OptimizerConfig()
        {
            LoadConfig();
        }

        public static void LoadConfig()
        {
            // First load local fallback
            ActiveConfig = LoadLocalConfig();

            // Try async download from GitHub repo asynchronously in background
            Task.Run(async () =>
            {
                try
                {
                    using (HttpClient client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(5);
                        HttpResponseMessage response = await client.GetAsync(REMOTE_URL);
                        if (!response.IsSuccessStatusCode)
                        {
                            Debug.LogWarning($"[OptimizerConfig] Remote config download failed (HTTP {(int)response.StatusCode} {response.ReasonPhrase}) — staying on local config.json.");
                            return;
                        }

                        string json = await response.Content.ReadAsStringAsync();
                        if (string.IsNullOrWhiteSpace(json))
                        {
                            Debug.LogWarning("[OptimizerConfig] Remote config returned empty response — staying on local config.json.");
                            return;
                        }

                        OptimizerConfigData remoteData = ParseAndValidateJson(json, "remote (GitHub)");
                        if (remoteData != null)
                        {
                            ActiveConfig = remoteData;
                            Debug.Log("[OptimizerConfig] Successfully fetched and validated updated config.json from GitHub repository.");
                        }
                        else
                        {
                            Debug.LogWarning("[OptimizerConfig] Remote config JSON validation failed — staying on local config.json.");
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    Debug.LogWarning($"[OptimizerConfig] Network request to GitHub failed ({ex.Message}) — staying on local config.json.");
                }
                catch (TaskCanceledException)
                {
                    Debug.LogWarning("[OptimizerConfig] Remote config download timed out (5s limit) — staying on local config.json.");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[OptimizerConfig] Unexpected error updating remote config: {ex.Message} — staying on local config.json.");
                }
            });
        }

        private static OptimizerConfigData LoadLocalConfig()
        {
            try
            {
                if (File.Exists(LOCAL_PATH))
                {
                    string json = File.ReadAllText(LOCAL_PATH);
                    OptimizerConfigData localData = ParseAndValidateJson(json, "local (Packages/dev.bluscream.avataroptimizer_vrc/config.json)");
                    if (localData != null) return localData;
                }
                else
                {
                    Debug.LogWarning($"[OptimizerConfig] Local config.json not found at '{LOCAL_PATH}' — using hardcoded defaults.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OptimizerConfig] Failed to read local config.json ({ex.Message}) — using hardcoded defaults.");
            }
            return new OptimizerConfigData();
        }

        private static OptimizerConfigData ParseAndValidateJson(string json, string sourceName)
        {
            try
            {
                OptimizerConfigData data = JsonUtility.FromJson<OptimizerConfigData>(json);
                if (data == null)
                {
                    Debug.LogWarning($"[OptimizerConfig] JsonUtility returned null when parsing {sourceName}.");
                    return null;
                }

                int warningCount = ValidateConfigData(data, sourceName);
                data.BuildLookupDictionaries();

                if (warningCount > 0)
                {
                    Debug.LogWarning($"[OptimizerConfig] {sourceName} loaded with {warningCount} validation warning(s).");
                }

                return data;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OptimizerConfig] JSON parse error in {sourceName}: {ex.Message}");
                return null;
            }
        }

        private static int ValidateConfigData(OptimizerConfigData data, string sourceName)
        {
            int warnings = 0;

            // Validate Shader Mapping Rules
            if (data.shaderMapping?.rules != null)
            {
                for (int i = data.shaderMapping.rules.Count - 1; i >= 0; i--)
                {
                    var rule = data.shaderMapping.rules[i];
                    if (rule == null)
                    {
                        data.shaderMapping.rules.RemoveAt(i);
                        warnings++;
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(rule.targetShader))
                    {
                        Debug.LogWarning($"[OptimizerConfig] [{sourceName}] Shader rule #{i} ('{rule.pattern}') has empty targetShader — removing invalid rule.");
                        data.shaderMapping.rules.RemoveAt(i);
                        warnings++;
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(rule.pattern) && (rule.requiredProperties == null || rule.requiredProperties.Count == 0))
                    {
                        Debug.LogWarning($"[OptimizerConfig] [{sourceName}] Shader rule #{i} has neither pattern nor requiredProperties specified — removing invalid rule.");
                        data.shaderMapping.rules.RemoveAt(i);
                        warnings++;
                        continue;
                    }

                    // Test compile regex if matchType is Regex
                    if (string.Equals(rule.matchType, "Regex", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(rule.pattern))
                    {
                        try
                        {
                            System.Text.RegularExpressions.Regex.IsMatch("", rule.pattern);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[OptimizerConfig] [{sourceName}] Shader rule #{i} has invalid regex ('{rule.pattern}'): {ex.Message} — removing invalid rule.");
                            data.shaderMapping.rules.RemoveAt(i);
                            warnings++;
                        }
                    }
                }
            }

            // Validate Platform Profile Limits (Out of Bounds checks)
            if (data.platformProfiles != null)
            {
                foreach (var platData in data.platformProfiles)
                {
                    if (platData?.ranks == null) continue;
                    foreach (var rankData in platData.ranks)
                    {
                        ProfileLimitData p = rankData?.limits;
                        if (p == null) continue;

                        // int.MaxValue / long.MaxValue are the "not specified — inherit from the
                        // platform base" sentinels, NOT real values. Clamping them turns an omitted
                        // field into an explicit one, which MergeWith then treats as an override:
                        // a rank that omitted MaxPhysBoneComponents would end up overriding the
                        // platform's real limit (e.g. Android Poor: 8 -> 256) and silently disable pruning.
                        if (p.MaxTriangles != int.MaxValue && p.MaxTriangles < 0)
                        {
                            Debug.LogWarning($"[OptimizerConfig] [{sourceName}] {platData.name}/{rankData.name} MaxTriangles is negative ({p.MaxTriangles}) — clamping to 0.");
                            p.MaxTriangles = 0;
                            warnings++;
                        }
                        if (p.MaxMaterialSlots != int.MaxValue && p.MaxMaterialSlots < 0)
                        {
                            Debug.LogWarning($"[OptimizerConfig] [{sourceName}] {platData.name}/{rankData.name} MaxMaterialSlots is negative ({p.MaxMaterialSlots}) — clamping to 0.");
                            p.MaxMaterialSlots = 0;
                            warnings++;
                        }
                        if (p.MaxTextureMemoryBytes != long.MaxValue && p.MaxTextureMemoryBytes < 0)
                        {
                            Debug.LogWarning($"[OptimizerConfig] [{sourceName}] {platData.name}/{rankData.name} MaxTextureMemoryBytes is negative ({p.MaxTextureMemoryBytes}) — clamping to 0.");
                            p.MaxTextureMemoryBytes = 0;
                            warnings++;
                        }
                        if (p.MaxPhysBoneComponents != int.MaxValue && (p.MaxPhysBoneComponents < 0 || p.MaxPhysBoneComponents > 256))
                        {
                            Debug.LogWarning($"[OptimizerConfig] [{sourceName}] {platData.name}/{rankData.name} MaxPhysBoneComponents ({p.MaxPhysBoneComponents}) is out of reasonable bounds [0-256] — clamping.");
                            p.MaxPhysBoneComponents = Mathf.Clamp(p.MaxPhysBoneComponents, 0, 256);
                            warnings++;
                        }
                    }
                }
            }

            return warnings;
        }
    }
}
