using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    [Serializable]
    public class PatternRuleData
    {
        public string pattern;
        public string replacement;
        public bool caseSensitive;
    }

    [Serializable]
    public class ShaderMappingData
    {
        public Dictionary<string, string> lookupTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<PatternRuleData> patternRules = new List<PatternRuleData>();
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
    public class OptimizerConfigData
    {
        public string version = "1.0.0";
        public ShaderMappingData shaderMapping = new ShaderMappingData();
        public Dictionary<string, Dictionary<string, ProfileLimitData>> platformProfiles = new Dictionary<string, Dictionary<string, ProfileLimitData>>();
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

            // Validate Shader Mapping Pattern Rules
            if (data.shaderMapping?.patternRules != null)
            {
                for (int i = data.shaderMapping.patternRules.Count - 1; i >= 0; i--)
                {
                    var rule = data.shaderMapping.patternRules[i];
                    if (string.IsNullOrWhiteSpace(rule.pattern))
                    {
                        Debug.LogWarning($"[OptimizerConfig] [{sourceName}] Pattern rule #{i} has an empty regex pattern — removing invalid rule.");
                        data.shaderMapping.patternRules.RemoveAt(i);
                        warnings++;
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(rule.replacement))
                    {
                        Debug.LogWarning($"[OptimizerConfig] [{sourceName}] Pattern rule #{i} ('{rule.pattern}') has an empty replacement shader — removing invalid rule.");
                        data.shaderMapping.patternRules.RemoveAt(i);
                        warnings++;
                        continue;
                    }

                    // Test compile regex pattern
                    try
                    {
                        System.Text.RegularExpressions.Regex.IsMatch("", rule.pattern);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[OptimizerConfig] [{sourceName}] Pattern rule #{i} has invalid regex ('{rule.pattern}'): {ex.Message} — removing invalid rule.");
                        data.shaderMapping.patternRules.RemoveAt(i);
                        warnings++;
                    }
                }
            }

            // Validate Shader Mapping Lookup Table
            if (data.shaderMapping?.lookupTable != null)
            {
                List<string> invalidKeys = new List<string>();
                foreach (var kvp in data.shaderMapping.lookupTable)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        Debug.LogWarning($"[OptimizerConfig] [{sourceName}] Lookup table entry '{kvp.Key}' -> '{kvp.Value}' has empty key/value — flagging for removal.");
                        invalidKeys.Add(kvp.Key);
                        warnings++;
                    }
                }
                foreach (string key in invalidKeys)
                {
                    data.shaderMapping.lookupTable.Remove(key);
                }
            }

            // Validate Platform Profile Limits (Out of Bounds checks)
            if (data.platformProfiles != null)
            {
                foreach (var platformKvp in data.platformProfiles)
                {
                    foreach (var rankKvp in platformKvp.Value)
                    {
                        ProfileLimitData p = rankKvp.Value;
                        if (p == null) continue;

                        if (p.MaxTriangles < 0)
                        {
                            Debug.LogWarning($"[OptimizerConfig] [{sourceName}] {platformKvp.Key}/{rankKvp.Key} MaxTriangles is negative ({p.MaxTriangles}) — clamping to 0.");
                            p.MaxTriangles = 0;
                            warnings++;
                        }
                        if (p.MaxMaterialSlots < 0)
                        {
                            Debug.LogWarning($"[OptimizerConfig] [{sourceName}] {platformKvp.Key}/{rankKvp.Key} MaxMaterialSlots is negative ({p.MaxMaterialSlots}) — clamping to 0.");
                            p.MaxMaterialSlots = 0;
                            warnings++;
                        }
                        if (p.MaxTextureMemoryBytes < 0)
                        {
                            Debug.LogWarning($"[OptimizerConfig] [{sourceName}] {platformKvp.Key}/{rankKvp.Key} MaxTextureMemoryBytes is negative ({p.MaxTextureMemoryBytes}) — clamping to 0.");
                            p.MaxTextureMemoryBytes = 0;
                            warnings++;
                        }
                        if (p.MaxPhysBoneComponents < 0 || p.MaxPhysBoneComponents > 256)
                        {
                            Debug.LogWarning($"[OptimizerConfig] [{sourceName}] {platformKvp.Key}/{rankKvp.Key} MaxPhysBoneComponents ({p.MaxPhysBoneComponents}) is out of reasonable bounds [0-256] — clamping.");
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
