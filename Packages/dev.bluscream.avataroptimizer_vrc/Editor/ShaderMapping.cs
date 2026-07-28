using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Handles shader replacement mapping from PC shaders to Quest-compatible shaders
    /// </summary>
    public static class ShaderMapping
    {
        // Quest-compatible shader paths
        private const string QUEST_TOON_STANDARD = "VRChat/Mobile/Toon Standard";
        private const string QUEST_TOON_LIT = "VRChat/Mobile/Toon Lit";
        private const string QUEST_STANDARD_LITE = "VRChat/Mobile/Standard Lite";
        private const string QUEST_BUMPED_DIFFUSE = "VRChat/Mobile/Bumped Diffuse";
        private const string QUEST_BUMPED_SPECULAR = "VRChat/Mobile/Bumped Mapped Specular";
        private const string QUEST_DIFFUSE = "VRChat/Mobile/Diffuse";
        private const string QUEST_MATCAP_LIT = "VRChat/Mobile/Matcap Lit";
        private const string QUEST_PARTICLES_ADDITIVE = "VRChat/Mobile/Particles/Additive";
        private const string QUEST_PARTICLES_MULTIPLY = "VRChat/Mobile/Particles/Multiply";

        /// <summary>
        /// Lookup table for exact shader name matches
        /// </summary>
        private static readonly Dictionary<string, string> ShaderLookupTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // lilToon shaders & variants
            { "liltoon", QUEST_TOON_STANDARD },
            { "liltoon/liltoon", QUEST_TOON_STANDARD },
            { "hidden/liltoonoutline", QUEST_TOON_STANDARD },
            { "hidden/liltooncutout", QUEST_TOON_STANDARD },
            { "hidden/liltoongem", QUEST_PARTICLES_MULTIPLY },
            { "hidden/liltoonrefraction", QUEST_PARTICLES_MULTIPLY },
            { "hidden/liltoonrefractionblur", QUEST_PARTICLES_MULTIPLY },
            { "hidden/liltoonlite", QUEST_TOON_STANDARD },
            { "hidden/liltoonlitetransparent", QUEST_PARTICLES_ADDITIVE },
            { "hidden/liltoontransparent", QUEST_PARTICLES_ADDITIVE },
            { "hidden/liltoonfur", QUEST_TOON_STANDARD },
            
            // Poiyomi shaders
            { "poiyomi/toon", QUEST_TOON_STANDARD },
            { "poiyomi/toon lite", QUEST_TOON_STANDARD },
            { "poiyomi/toon lit", QUEST_TOON_LIT },
            { "poiyomi/toon unlit", QUEST_TOON_LIT },
            { "poiyomi/toon standard", QUEST_TOON_STANDARD },
            { "poiyomi/pro", QUEST_TOON_STANDARD },
            
            // Unity Standard shaders
            { "standard", QUEST_STANDARD_LITE },
            { "standard (specular setup)", QUEST_BUMPED_SPECULAR },
            { "standard (roughness setup)", QUEST_STANDARD_LITE },
            
            // Unity Legacy shaders
            { "diffuse", QUEST_DIFFUSE },
            { "bumped diffuse", QUEST_BUMPED_DIFFUSE },
            { "bumped specular", QUEST_BUMPED_SPECULAR },
            { "specular", QUEST_BUMPED_SPECULAR },
            
            // Particle shaders
            { "particles/additive", QUEST_PARTICLES_ADDITIVE },
            { "particles/additive (soft)", QUEST_PARTICLES_ADDITIVE },
            { "particles/alpha blended", QUEST_PARTICLES_ADDITIVE },
            { "particles/alpha blended premultiply", QUEST_PARTICLES_ADDITIVE },
            { "particles/vertexlit blended", QUEST_PARTICLES_ADDITIVE },
            { "particles/standard surface", QUEST_PARTICLES_ADDITIVE },
            { "particles/standard unlit", QUEST_PARTICLES_ADDITIVE },
            { "particles/multiply", QUEST_PARTICLES_MULTIPLY },
            { "particles/multiply (double)", QUEST_PARTICLES_MULTIPLY },
        };

        /// <summary>
        /// Pattern-based matching rules (checked if exact match not found)
        /// </summary>
        private static readonly List<(string pattern, string replacement, bool caseSensitive)> PatternRules = new List<(string, string, bool)>
        {
            // ── Brand/family-specific patterns FIRST:
            // 1. Particle / Transparency variants within Poiyomi & lilToon
            (".*poiyomi.*particle.*", QUEST_PARTICLES_ADDITIVE, false),
            (".*poiyomi.*outline.*", QUEST_TOON_STANDARD, false),
            (".*poiyomi.*wireframe.*", QUEST_TOON_LIT, false),
            (".*poiyomi.*grab.*", QUEST_PARTICLES_MULTIPLY, false),
            (".*poiyomi.*dissolve.*", QUEST_TOON_STANDARD, false),
            (".*poiyomi.*toon.*", QUEST_TOON_STANDARD, false),
            (".*poiyomi.*unlit.*", QUEST_TOON_LIT, false),
            (".*poiyomi.*lit.*", QUEST_TOON_LIT, false),
            (".*poiyomi.*", QUEST_TOON_STANDARD, false),

            (".*liltoon.*gem.*", QUEST_PARTICLES_MULTIPLY, false),
            (".*liltoon.*refraction.*", QUEST_PARTICLES_MULTIPLY, false),
            (".*liltoon.*transparent.*", QUEST_PARTICLES_ADDITIVE, false),
            (".*liltoon.*cutout.*", QUEST_TOON_STANDARD, false),
            (".*liltoon.*outline.*", QUEST_TOON_STANDARD, false),
            (".*liltoon.*fur.*", QUEST_TOON_STANDARD, false),
            (".*liltoon.*", QUEST_TOON_STANDARD, false),

            // Custom HUD & Special Effect Brand Shaders (Ikeiwa IkeHUD, Hologram, Scanner)
            (".*ikehud.*visor.*", QUEST_PARTICLES_MULTIPLY, false),
            (".*ikehud.*overlay.*", QUEST_PARTICLES_ADDITIVE, false),
            (".*ikehud.*", QUEST_PARTICLES_ADDITIVE, false),
            (".*hologram.*", QUEST_PARTICLES_ADDITIVE, false),
            (".*scanner.*", QUEST_PARTICLES_MULTIPLY, false),

            // Toon shader patterns
            (".*toon.*standard.*", QUEST_TOON_STANDARD, false),
            (".*toon.*unlit.*", QUEST_TOON_LIT, false),
            (".*toon.*lit.*", QUEST_TOON_LIT, false),
            (".*toon.*", QUEST_TOON_STANDARD, false),

            // Matcap patterns
            (".*matcap.*", QUEST_MATCAP_LIT, false),

            // ── Actual particle shader paths ("Particles/…")
            (".*particles?/.*multiply.*", QUEST_PARTICLES_MULTIPLY, false),
            (".*particles?/.*", QUEST_PARTICLES_ADDITIVE, false),

            // ── Standard shader patterns
            (".*standard.*metallic.*", QUEST_STANDARD_LITE, false),
            (".*standard.*specular.*", QUEST_BUMPED_SPECULAR, false),
            (".*standard.*", QUEST_STANDARD_LITE, false),

            // Diffuse patterns
            (".*diffuse.*bump.*", QUEST_BUMPED_DIFFUSE, false),
            (".*diffuse.*normal.*", QUEST_BUMPED_DIFFUSE, false),
            (".*diffuse.*", QUEST_DIFFUSE, false),

            // Specular patterns
            (".*specular.*bump.*", QUEST_BUMPED_SPECULAR, false),
            (".*specular.*normal.*", QUEST_BUMPED_SPECULAR, false),
            (".*specular.*", QUEST_BUMPED_SPECULAR, false),

            // ── Special-effect / transparency patterns LAST — catch-alls for glow cones, visors,
            //    HUDs etc. Word-boundary guards keep 'cone' from matching 'silicone' and
            //    'glass' from matching 'sunglasses'.
            (".*flashlight.*", QUEST_PARTICLES_MULTIPLY, false),
            (".*light.*beam.*", QUEST_PARTICLES_MULTIPLY, false),
            (@".*(?:^|[\W_])volume(?:[\W_]|$).*", QUEST_PARTICLES_MULTIPLY, false),
            (@".*(?:^|[\W_])cone(?:[\W_]|$).*", QUEST_PARTICLES_MULTIPLY, false),
            (@".*(?:^|[\W_])dome(?:[\W_]|$).*", QUEST_PARTICLES_ADDITIVE, false),
            (".*safespace.*", QUEST_PARTICLES_ADDITIVE, false),
            (@".*(?:^|[\W_])visor(?:[\W_]|$).*", QUEST_PARTICLES_MULTIPLY, false),
            (@".*(?:^|[\W_])hud(?:[\W_]|$).*", QUEST_PARTICLES_ADDITIVE, false),
            (".*censor.*", QUEST_PARTICLES_MULTIPLY, false),
            (".*pixelate.*", QUEST_PARTICLES_MULTIPLY, false),
            (".*blocker.*", QUEST_PARTICLES_ADDITIVE, false), // disabled due to being "Delete on upload" anyway
            (".*squish.*", QUEST_PARTICLES_MULTIPLY, false),
            (@".*(?:^|[\W_])lens(?:es)?(?:[\W_]|$).*", QUEST_PARTICLES_MULTIPLY, false),
            (@".*(?:^|[\W_])eye(?:s)?(?:[\W_]|$).*", QUEST_PARTICLES_MULTIPLY, false),
            (@".*(?:^|[\W_])glass(?:[\W_]|$).*", QUEST_PARTICLES_MULTIPLY, false),
            (".*transparent.*multiply.*", QUEST_PARTICLES_MULTIPLY, false),
            (".*transparent.*", QUEST_PARTICLES_ADDITIVE, false),
            (".*additive.*", QUEST_PARTICLES_ADDITIVE, false),
            (".*glow.*", QUEST_PARTICLES_ADDITIVE, false),
        };

        /// <summary>
        /// Finds a Quest-compatible replacement shader for the given shader name
        /// </summary>
        public static ShaderReplacementResult FindReplacementShader(string originalShaderName)
        {
            if (string.IsNullOrEmpty(originalShaderName))
            {
                return new ShaderReplacementResult
                {
                    Success = false,
                    Reason = "Shader name is null or empty"
                };
            }

            // Check if already a Quest shader
            if (originalShaderName.StartsWith("VRChat/Mobile/", StringComparison.OrdinalIgnoreCase))
            {
                return new ShaderReplacementResult
                {
                    Success = false,
                    Reason = "Shader is already Quest-compatible",
                    IsAlreadyCompatible = true
                };
            }

            // Try exact lookup from OptimizerConfig JSON first, then static lookup table
            string exactMatch = null;
            if (OptimizerConfig.ActiveConfig?.LookupDict != null &&
                OptimizerConfig.ActiveConfig.LookupDict.TryGetValue(originalShaderName, out exactMatch))
            {
                Shader shader = Shader.Find(exactMatch);
                if (shader != null)
                {
                    return new ShaderReplacementResult
                    {
                        Success = true,
                        ReplacementShader = shader,
                        ReplacementShaderName = exactMatch,
                        MatchType = "Config JSON Exact lookup"
                    };
                }
            }

            if (ShaderLookupTable.TryGetValue(originalShaderName, out exactMatch))
            {
                Shader shader = Shader.Find(exactMatch);
                if (shader != null)
                {
                    return new ShaderReplacementResult
                    {
                        Success = true,
                        ReplacementShader = shader,
                        ReplacementShaderName = exactMatch,
                        MatchType = "Exact lookup"
                    };
                }
            }

            // Try pattern matching from OptimizerConfig JSON first, then static rules
            string lowerName = originalShaderName.ToLowerInvariant();
            if (OptimizerConfig.ActiveConfig?.shaderMapping?.patternRules != null)
            {
                foreach (var rule in OptimizerConfig.ActiveConfig.shaderMapping.patternRules)
                {
                    try
                    {
                        string nameToCheck = rule.caseSensitive ? originalShaderName : lowerName;
                        if (System.Text.RegularExpressions.Regex.IsMatch(nameToCheck, rule.pattern))
                        {
                            Shader shader = Shader.Find(rule.replacement);
                            if (shader != null)
                            {
                                return new ShaderReplacementResult
                                {
                                    Success = true,
                                    ReplacementShader = shader,
                                    ReplacementShaderName = rule.replacement,
                                    MatchType = $"Config JSON Pattern: {rule.pattern}"
                                };
                            }
                        }
                    }
                    catch { continue; }
                }
            }

            foreach (var (pattern, replacement, caseSensitive) in PatternRules)
            {
                try
                {
                    string nameToCheck = caseSensitive ? originalShaderName : lowerName;
                    if (System.Text.RegularExpressions.Regex.IsMatch(nameToCheck, pattern))
                    {
                        Shader shader = Shader.Find(replacement);
                        if (shader != null)
                        {
                            return new ShaderReplacementResult
                            {
                                Success = true,
                                ReplacementShader = shader,
                                ReplacementShaderName = replacement,
                                MatchType = $"Pattern: {pattern}"
                            };
                        }
                    }
                }
                catch (Exception)
                {
                    // Invalid regex pattern, skip
                    continue;
                }
            }

            // Try automatic keyword-based matching
            return TryAutomaticMatching(originalShaderName);
        }

        /// <summary>
        /// Attempts automatic shader matching based on keywords
        /// </summary>
        private static ShaderReplacementResult TryAutomaticMatching(string shaderName)
        {
            string lowerName = shaderName.ToLowerInvariant();
            
            // Check for keywords in order of specificity
            if (lowerName.Contains("toon"))
            {
                if (lowerName.Contains("lit") || lowerName.Contains("unlit"))
                {
                    Shader shader = Shader.Find(QUEST_TOON_LIT);
                    if (shader != null)
                        return new ShaderReplacementResult { Success = true, ReplacementShader = shader, ReplacementShaderName = QUEST_TOON_LIT, MatchType = "Keyword: toon+lit" };
                }
                
                Shader toonShader = Shader.Find(QUEST_TOON_STANDARD);
                if (toonShader != null)
                    return new ShaderReplacementResult { Success = true, ReplacementShader = toonShader, ReplacementShaderName = QUEST_TOON_STANDARD, MatchType = "Keyword: toon" };
            }
            
            if (lowerName.Contains("specular"))
            {
                Shader shader = Shader.Find(QUEST_BUMPED_SPECULAR);
                if (shader != null)
                    return new ShaderReplacementResult { Success = true, ReplacementShader = shader, ReplacementShaderName = QUEST_BUMPED_SPECULAR, MatchType = "Keyword: specular" };
            }
            
            if (lowerName.Contains("bump") || lowerName.Contains("normal"))
            {
                Shader shader = Shader.Find(QUEST_BUMPED_DIFFUSE);
                if (shader != null)
                    return new ShaderReplacementResult { Success = true, ReplacementShader = shader, ReplacementShaderName = QUEST_BUMPED_DIFFUSE, MatchType = "Keyword: bump/normal" };
            }
            
            if (lowerName.Contains("standard") || lowerName.Contains("metallic"))
            {
                Shader shader = Shader.Find(QUEST_STANDARD_LITE);
                if (shader != null)
                    return new ShaderReplacementResult { Success = true, ReplacementShader = shader, ReplacementShaderName = QUEST_STANDARD_LITE, MatchType = "Keyword: standard/metallic" };
            }
            
            if (lowerName.Contains("matcap"))
            {
                Shader shader = Shader.Find(QUEST_MATCAP_LIT);
                if (shader != null)
                    return new ShaderReplacementResult { Success = true, ReplacementShader = shader, ReplacementShaderName = QUEST_MATCAP_LIT, MatchType = "Keyword: matcap" };
            }
            
            if (lowerName.Contains("particle"))
            {
                if (lowerName.Contains("multiply"))
                {
                    Shader shader = Shader.Find(QUEST_PARTICLES_MULTIPLY);
                    if (shader != null)
                        return new ShaderReplacementResult { Success = true, ReplacementShader = shader, ReplacementShaderName = QUEST_PARTICLES_MULTIPLY, MatchType = "Keyword: particle+multiply" };
                }
                
                Shader shader2 = Shader.Find(QUEST_PARTICLES_ADDITIVE);
                if (shader2 != null)
                    return new ShaderReplacementResult { Success = true, ReplacementShader = shader2, ReplacementShaderName = QUEST_PARTICLES_ADDITIVE, MatchType = "Keyword: particle" };
            }
            
            // Final fallback: try Diffuse
            Shader diffuseShader = Shader.Find(QUEST_DIFFUSE);
            if (diffuseShader != null)
            {
                return new ShaderReplacementResult
                {
                    Success = true,
                    ReplacementShader = diffuseShader,
                    ReplacementShaderName = QUEST_DIFFUSE,
                    MatchType = "Fallback: Diffuse"
                };
            }
            
            return new ShaderReplacementResult
            {
                Success = false,
                Reason = "No matching Quest shader found"
            };
        }

        /// <summary>
        /// Result of shader replacement lookup
        /// </summary>
        public class ShaderReplacementResult
        {
            public bool Success { get; set; }
            public Shader ReplacementShader { get; set; }
            public string ReplacementShaderName { get; set; }
            public string MatchType { get; set; }
            public string Reason { get; set; }
            public bool IsAlreadyCompatible { get; set; }
        }
    }
}
