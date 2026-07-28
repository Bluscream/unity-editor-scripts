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
                        string json = await client.GetStringAsync(REMOTE_URL);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            OptimizerConfigData remoteData = JsonUtility.FromJson<OptimizerConfigData>(json);
                            if (remoteData != null)
                            {
                                ActiveConfig = remoteData;
                                Debug.Log("<color=lime><b>[OptimizerConfig]</b></color> Successfully updated config from GitHub repository.");
                            }
                        }
                    }
                }
                catch
                {
                    // Silent fallback to local version on network error or offline mode
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
                    OptimizerConfigData localData = JsonUtility.FromJson<OptimizerConfigData>(json);
                    if (localData != null) return localData;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OptimizerConfig] Failed to load local config.json ({ex.Message}) — using default hardcoded fallback.");
            }
            return new OptimizerConfigData();
        }
    }
}
