using System;
using System.Collections.Generic;
using System.IO;
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
        public string name;
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
        public int MaxTriangles = int.MaxValue;
        public int MaxMaterialSlots = int.MaxValue;
        public int MaxPhysBoneComponents = 8;
        public int MaxPhysBoneTransforms = 64;
        public int MaxPhysBoneColliders = 16;
        public int MaxPhysBoneCollisionChecks = 64;
        public long MaxTextureMemoryBytes = 40 * 1024 * 1024L;
    }

    [Serializable]
    public class RankProfileData
    {
        public string rank;
        public ProfileLimitData limits = new ProfileLimitData();
    }

    [Serializable]
    public class PlatformProfileData
    {
        public string platform;
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
                    if (string.IsNullOrWhiteSpace(platData.platform) || platData.ranks == null) continue;
                    var rankDict = new Dictionary<string, ProfileLimitData>(StringComparer.OrdinalIgnoreCase);
                    foreach (var rankData in platData.ranks)
                    {
                        if (!string.IsNullOrWhiteSpace(rankData.rank) && rankData.limits != null)
                        {
                            rankDict[rankData.rank] = rankData.limits;
                        }
                    }
                    ProfileDict[platData.platform] = rankDict;
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
                        Debug.LogWarning($"[OptimizerConfig] [{sourceName}] Shader rule #{i} ('{rule.name}') has empty targetShader — removing invalid rule.");
                        data.shaderMapping.rules.RemoveAt(i);
                        warnings++;
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(rule.pattern) && (rule.requiredProperties == null || rule.requiredProperties.Count == 0))
                    {
                        Debug.LogWarning($"[OptimizerConfig] [{sourceName}] Shader rule #{i} ('{rule.name}') has neither pattern nor requiredProperties specified — removing invalid rule.");
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
                            Debug.LogWarning($"[OptimizerConfig] [{sourceName}] Shader rule #{i} ('{rule.name}') has invalid regex ('{rule.pattern}'): {ex.Message} — removing invalid rule.");
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

                        if (p.MaxTriangles < 0)
                        {
                            Debug.LogWarning($"[OptimizerConfig] [{sourceName}] {platData.platform}/{rankData.rank} MaxTriangles is negative ({p.MaxTriangles}) — clamping to 0.");
                            p.MaxTriangles = 0;
                            warnings++;
                        }
                        if (p.MaxMaterialSlots < 0)
                        {
                            Debug.LogWarning($"[OptimizerConfig] [{sourceName}] {platData.platform}/{rankData.rank} MaxMaterialSlots is negative ({p.MaxMaterialSlots}) — clamping to 0.");
                            p.MaxMaterialSlots = 0;
                            warnings++;
                        }
                        if (p.MaxTextureMemoryBytes < 0)
                        {
                            Debug.LogWarning($"[OptimizerConfig] [{sourceName}] {platData.platform}/{rankData.rank} MaxTextureMemoryBytes is negative ({p.MaxTextureMemoryBytes}) — clamping to 0.");
                            p.MaxTextureMemoryBytes = 0;
                            warnings++;
                        }
                        if (p.MaxPhysBoneComponents < 0 || p.MaxPhysBoneComponents > 256)
                        {
                            Debug.LogWarning($"[OptimizerConfig] [{sourceName}] {platData.platform}/{rankData.rank} MaxPhysBoneComponents ({p.MaxPhysBoneComponents}) is out of reasonable bounds [0-256] — clamping.");
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
