using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using static Bluscream.Utils;

namespace VRCQuestPatcher
{
    /// <summary>
    /// Maps and transfers shader properties from source shaders to target shaders
    /// </summary>
    public static class ShaderPropertyMapper
    {
        [System.Serializable]
        private class PropertyMapping
        {
            public string source;
            public string target;
            public string description;
        }

        [System.Serializable]
        private class PropertyMappingsData
        {
            public string version;
            public string description;
            public PropertyMapping[] universalMappings;
            public string[] ignoredProperties;
            public string[] ignoredPropertyPrefixes;
            public string[] ignoredPropertySuffixes;
        }

        /// <summary>
        /// Property mapping rules: source property name -> target property name
        /// </summary>
        private static Dictionary<string, string> UniversalPropertyMappings;
        
        /// <summary>
        /// Properties to ignore when transferring (shader-specific, not supported in VRChat Mobile)
        /// </summary>
        private static HashSet<string> IgnoredProperties;
        
        /// <summary>
        /// Property name prefixes to ignore
        /// </summary>
        private static HashSet<string> IgnoredPropertyPrefixes;
        
        /// <summary>
        /// Property name suffixes to ignore
        /// </summary>
        private static HashSet<string> IgnoredPropertySuffixes;

        private static bool initialized = false;

        /// <summary>
        /// Initialize mappings from JSON file
        /// </summary>
        private static void Initialize()
        {
            if (initialized) return;
            
            // Initialize with defaults
            UniversalPropertyMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IgnoredProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IgnoredPropertyPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IgnoredPropertySuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // Try to load from JSON
            try
            {
                TextAsset jsonFile = Resources.Load<TextAsset>("ShaderPropertyMappings");
                if (jsonFile != null)
                {
                    PropertyMappingsData data = JsonUtility.FromJson<PropertyMappingsData>(jsonFile.text);
                    if (data != null)
                    {
                        // Load universal mappings
                        if (data.universalMappings != null)
                        {
                            foreach (var mapping in data.universalMappings)
                            {
                                if (!string.IsNullOrEmpty(mapping.source) && !string.IsNullOrEmpty(mapping.target))
                                {
                                    UniversalPropertyMappings[mapping.source] = mapping.target;
                                }
                            }
                        }
                        
                        // Load ignored properties
                        if (data.ignoredProperties != null)
                        {
                            foreach (var prop in data.ignoredProperties)
                            {
                                if (!string.IsNullOrEmpty(prop))
                                    IgnoredProperties.Add(prop);
                            }
                        }
                        
                        // Load ignored prefixes
                        if (data.ignoredPropertyPrefixes != null)
                        {
                            foreach (var prefix in data.ignoredPropertyPrefixes)
                            {
                                if (!string.IsNullOrEmpty(prefix))
                                    IgnoredPropertyPrefixes.Add(prefix);
                            }
                        }
                        
                        // Load ignored suffixes
                        if (data.ignoredPropertySuffixes != null)
                        {
                            foreach (var suffix in data.ignoredPropertySuffixes)
                            {
                                if (!string.IsNullOrEmpty(suffix))
                                    IgnoredPropertySuffixes.Add(suffix);
                            }
                        }
                        
                        Debug.Log($"Loaded {UniversalPropertyMappings.Count} property mappings from ShaderPropertyMappings.json");
                        initialized = true;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to load ShaderPropertyMappings.json, using defaults: {e.Message}");
            }
            
            // Fallback to hardcoded defaults if JSON loading fails
            LoadDefaultMappings();
            initialized = true;
        }

        /// <summary>
        /// Load default hardcoded mappings as fallback
        /// </summary>
        private static void LoadDefaultMappings()
        {
            // Universal properties that work across most shaders
            UniversalPropertyMappings["_MainTex"] = "_MainTex";
            UniversalPropertyMappings["_Color"] = "_Color";
            UniversalPropertyMappings["_BumpMap"] = "_BumpMap";
            UniversalPropertyMappings["_BumpScale"] = "_BumpScale";
            UniversalPropertyMappings["_NormalMap"] = "_BumpMap";
            UniversalPropertyMappings["_NormalScale"] = "_BumpScale";
            UniversalPropertyMappings["_Cutoff"] = "_Cutoff";
            UniversalPropertyMappings["_AlphaCutoff"] = "_Cutoff";
            
            // Emission properties
            UniversalPropertyMappings["_EmissionMap"] = "_EmissionMap";
            UniversalPropertyMappings["_EmissionColor"] = "_EmissionColor";
            UniversalPropertyMappings["_Emission"] = "_EmissionColor";
            
            // Metallic/Smoothness
            UniversalPropertyMappings["_MetallicGlossMap"] = "_MetallicGlossMap";
            UniversalPropertyMappings["_Metallic"] = "_Metallic";
            UniversalPropertyMappings["_Glossiness"] = "_Glossiness";
            UniversalPropertyMappings["_Smoothness"] = "_Glossiness";
            UniversalPropertyMappings["_GlossMapScale"] = "_Glossiness";
            
            // Occlusion
            UniversalPropertyMappings["_OcclusionMap"] = "_OcclusionMap";
            UniversalPropertyMappings["_OcclusionStrength"] = "_OcclusionStrength";
            
            // Detail maps
            UniversalPropertyMappings["_DetailAlbedoMap"] = "_DetailAlbedoMap";
            UniversalPropertyMappings["_DetailTex"] = "_DetailAlbedoMap";
            UniversalPropertyMappings["_DetailNormalMap"] = "_DetailNormalMap";
            UniversalPropertyMappings["_DetailNormalMapScale"] = "_DetailNormalMapScale";
            UniversalPropertyMappings["_DetailMask"] = "_DetailMask";
            
            // Specular
            UniversalPropertyMappings["_SpecGlossMap"] = "_SpecGlossMap";
            UniversalPropertyMappings["_SpecColor"] = "_SpecColor";
            
            // Tiling and Offset
            UniversalPropertyMappings["_MainTex_ST"] = "_MainTex_ST";
            UniversalPropertyMappings["_BumpMap_ST"] = "_BumpMap_ST";
            UniversalPropertyMappings["_DetailAlbedoMap_ST"] = "_DetailAlbedoMap_ST";
            
            // Default ignored properties
            IgnoredProperties.Add("shader_master_label");
            IgnoredProperties.Add("shader_is_using_thry_editor");
            IgnoredProperties.Add("shader_locale");
            
            // Default ignored prefixes
            IgnoredPropertyPrefixes.Add("m_start_");
            IgnoredPropertyPrefixes.Add("m_end_");
            IgnoredPropertyPrefixes.Add("s_start_");
            IgnoredPropertyPrefixes.Add("s_end_");
            IgnoredPropertyPrefixes.Add("footer_");
            IgnoredPropertyPrefixes.Add("_ShaderUI");
            
            // Default ignored suffixes
            IgnoredPropertySuffixes.Add("Pan");
            IgnoredPropertySuffixes.Add("UV");
            IgnoredPropertySuffixes.Add("Stochastic");
            IgnoredPropertySuffixes.Add("PixelMode");
            IgnoredPropertySuffixes.Add("ThemeIndex");
            IgnoredPropertySuffixes.Add("GlobalMask");
            IgnoredPropertySuffixes.Add("BlendType");
            IgnoredPropertySuffixes.Add("Toggle");
            IgnoredPropertySuffixes.Add("Enabled");
        }

        /// <summary>
        /// Transfers compatible properties from source material to target material after shader replacement
        /// </summary>
        public static PropertyTransferResult TransferProperties(Material sourceMaterial, Material targetMaterial, Shader targetShader)
        {
            Initialize(); // Ensure mappings are loaded
            
            PropertyTransferResult result = new PropertyTransferResult();
            
            if (sourceMaterial == null || targetMaterial == null || targetShader == null)
            {
                result.Success = false;
                result.Reason = "Source material, target material, or target shader is null";
                return result;
            }

            // Get all properties from the source material's backup (if available)
            // For now, we'll work with the material directly
            try
            {
                #if UNITY_2021_2_OR_NEWER
                // Get source shader properties
                int sourcePropertyCount = UnityEditor.ShaderUtil.GetPropertyCount(sourceMaterial.shader);
                for (int i = 0; i < sourcePropertyCount; i++)
                {
                    string sourcePropertyName = UnityEditor.ShaderUtil.GetPropertyName(sourceMaterial.shader, i);
                    UnityEditor.ShaderUtil.ShaderPropertyType sourcePropertyType = UnityEditor.ShaderUtil.GetPropertyType(sourceMaterial.shader, i);
                    
                    // Skip ignored properties
                    if (ShouldIgnoreProperty(sourcePropertyName))
                        continue;
                    
                    // Find target property name
                    string targetPropertyName = GetTargetPropertyName(sourcePropertyName, targetShader);
                    if (string.IsNullOrEmpty(targetPropertyName))
                        continue;
                    
                    // Check if target shader has this property
                    if (!targetMaterial.HasProperty(targetPropertyName))
                        continue;
                    
                    // Transfer the property value
                    try
                    {
                        TransferPropertyValue(sourceMaterial, targetMaterial, sourcePropertyName, targetPropertyName, sourcePropertyType);
                        result.PropertiesTransferred++;
                        result.TransferredProperties.Add($"{sourcePropertyName} → {targetPropertyName}");
                    }
                    catch (Exception e)
                    {
                        result.PropertiesFailed++;
                        result.FailedProperties.Add($"{sourcePropertyName}: {e.Message}");
                    }
                }
                #else
                // For older Unity versions, transfer common properties manually
                TransferCommonProperties(sourceMaterial, targetMaterial, targetShader, result);
                #endif
                
                result.Success = true;
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Reason = $"Error transferring properties: {e.Message}";
            }
            
            return result;
        }

        /// <summary>
        /// Transfers properties from backup data to a material with a new shader
        /// </summary>
        public static PropertyTransferResult TransferPropertiesFromBackup(
            BackupManager.MaterialBackup backup,
            Material targetMaterial,
            Shader targetShader)
        {
            Initialize(); // Ensure mappings are loaded
            
            PropertyTransferResult result = new PropertyTransferResult();
            
            if (backup == null || targetMaterial == null || targetShader == null)
            {
                result.Success = false;
                result.Reason = "Backup, target material, or target shader is null";
                return result;
            }

            if (backup.materialProperties == null || backup.materialProperties.Count == 0)
            {
                result.Success = true; // No properties to transfer, but that's okay
                return result;
            }

            try
            {
                foreach (var entry in backup.materialProperties)
                {
                    // Skip ignored properties
                    if (ShouldIgnoreProperty(entry.propertyName))
                        continue;
                    
                    // Find target property name
                    string targetPropertyName = GetTargetPropertyName(entry.propertyName, targetShader);
                    if (string.IsNullOrEmpty(targetPropertyName))
                        continue;
                    
                    // Check if target shader has this property
                    if (!targetMaterial.HasProperty(targetPropertyName))
                        continue;
                    
                    // Transfer the property value
                    try
                    {
                        SetPropertyFromBackupEntry(targetMaterial, targetPropertyName, entry);
                        result.PropertiesTransferred++;
                        result.TransferredProperties.Add($"{entry.propertyName} → {targetPropertyName}");
                    }
                    catch (Exception e)
                    {
                        result.PropertiesFailed++;
                        result.FailedProperties.Add($"{entry.propertyName}: {e.Message}");
                    }
                }
                
                result.Success = true;
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Reason = $"Error transferring properties from backup: {e.Message}";
            }
            
            return result;
        }

        private static bool ShouldIgnoreProperty(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                return true;
            
            Initialize(); // Ensure mappings are loaded
            
            // Check exact matches
            if (IgnoredProperties.Contains(propertyName))
                return true;
            
            // Check prefix matches
            foreach (var ignoredPrefix in IgnoredPropertyPrefixes)
            {
                if (propertyName.StartsWith(ignoredPrefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            
            // Check suffix matches
            foreach (var ignoredSuffix in IgnoredPropertySuffixes)
            {
                if (propertyName.EndsWith(ignoredSuffix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            
            return false;
        }

        private static string GetTargetPropertyName(string sourcePropertyName, Shader targetShader)
        {
            // Check universal mappings first
            if (UniversalPropertyMappings.TryGetValue(sourcePropertyName, out string mappedName))
            {
                // Verify target shader has this property
                #if UNITY_2021_2_OR_NEWER
                int propertyCount = UnityEditor.ShaderUtil.GetPropertyCount(targetShader);
                for (int i = 0; i < propertyCount; i++)
                {
                    if (UnityEditor.ShaderUtil.GetPropertyName(targetShader, i).Equals(mappedName, StringComparison.OrdinalIgnoreCase))
                        return mappedName;
                }
                #endif
            }
            
            // If direct mapping doesn't exist in target, try exact match
            #if UNITY_2021_2_OR_NEWER
            int targetPropertyCount = UnityEditor.ShaderUtil.GetPropertyCount(targetShader);
            for (int i = 0; i < targetPropertyCount; i++)
            {
                string targetPropName = UnityEditor.ShaderUtil.GetPropertyName(targetShader, i);
                if (targetPropName.Equals(sourcePropertyName, StringComparison.OrdinalIgnoreCase))
                    return targetPropName;
            }
            #endif
            
            return null;
        }

        private static void TransferPropertyValue(
            Material source,
            Material target,
            string sourcePropertyName,
            string targetPropertyName,
            UnityEditor.ShaderUtil.ShaderPropertyType propertyType)
        {
            switch (propertyType)
            {
                case UnityEditor.ShaderUtil.ShaderPropertyType.Color:
                    target.SetColor(targetPropertyName, source.GetColor(sourcePropertyName));
                    break;
                    
                case UnityEditor.ShaderUtil.ShaderPropertyType.Vector:
                    target.SetVector(targetPropertyName, source.GetVector(sourcePropertyName));
                    break;
                    
                case UnityEditor.ShaderUtil.ShaderPropertyType.Float:
                case UnityEditor.ShaderUtil.ShaderPropertyType.Range:
                    target.SetFloat(targetPropertyName, source.GetFloat(sourcePropertyName));
                    break;
                    
                case UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv:
                    Texture tex = source.GetTexture(sourcePropertyName);
                    if (tex != null)
                        target.SetTexture(targetPropertyName, tex);
                    break;
            }
        }

        private static void SetPropertyFromBackupEntry(
            Material target,
            string targetPropertyName,
            BackupManager.MaterialPropertyEntry entry)
        {
            switch (entry.propertyType)
            {
                case "color":
                    if (TryParseColor(entry.propertyValue, out Color colorValue))
                        target.SetColor(targetPropertyName, colorValue);
                    break;
                    
                case "vector":
                    if (TryParseVector(entry.propertyValue, out Vector4 vectorValue))
                        target.SetVector(targetPropertyName, vectorValue);
                    break;
                    
                case "float":
                    if (float.TryParse(entry.propertyValue, out float floatValue))
                        target.SetFloat(targetPropertyName, floatValue);
                    break;
                    
                case "texture":
                    Texture tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture>(entry.propertyValue);
                    if (tex != null)
                        target.SetTexture(targetPropertyName, tex);
                    break;
            }
        }

        private static void TransferCommonProperties(
            Material source,
            Material target,
            Shader targetShader,
            PropertyTransferResult result)
        {
            string[] commonProperties = { "_MainTex", "_Color", "_BumpMap", "_BumpScale", "_EmissionMap", "_EmissionColor", "_Cutoff" };
            
            foreach (string propName in commonProperties)
            {
                if (source.HasProperty(propName) && target.HasProperty(propName))
                {
                    try
                    {
                        if (propName.Contains("Tex") || propName.Contains("Map"))
                        {
                            Texture tex = source.GetTexture(propName);
                            if (tex != null)
                            {
                                target.SetTexture(propName, tex);
                                result.PropertiesTransferred++;
                            }
                        }
                        else if (propName.Contains("Color"))
                        {
                            target.SetColor(propName, source.GetColor(propName));
                            result.PropertiesTransferred++;
                        }
                        else
                        {
                            target.SetFloat(propName, source.GetFloat(propName));
                            result.PropertiesTransferred++;
                        }
                    }
                    catch
                    {
                        result.PropertiesFailed++;
                    }
                }
            }
        }


        /// <summary>
        /// Result of property transfer operation
        /// </summary>
        public class PropertyTransferResult
        {
            public bool Success { get; set; }
            public string Reason { get; set; }
            public int PropertiesTransferred { get; set; }
            public int PropertiesFailed { get; set; }
            public List<string> TransferredProperties { get; set; } = new List<string>();
            public List<string> FailedProperties { get; set; } = new List<string>();
        }
    }
}
