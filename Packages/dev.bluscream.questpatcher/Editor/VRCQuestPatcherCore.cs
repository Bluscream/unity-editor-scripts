using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRCQuestPatcher
{
    /// <summary>
    /// Core conversion logic that orchestrates the Quest patching process
    /// </summary>
    public static class VRCQuestPatcherCore
    {
        /// <summary>
        /// Configuration for the conversion process
        /// </summary>
        public class ConversionConfig
        {
            public bool removeComponents = true;
            public bool replaceShaders = true;
            public bool optimizeTextures = false;
            public int maxTextureSize = 1024;
            public int compressionQuality = 75;
            public bool useCrunchCompression = true;
            public string backupLocation = "Assets/VRCQuestPatcherBackups";
        }

        /// <summary>
        /// Performs the complete conversion process
        /// </summary>
        public static ConversionSummary ConvertAvatar(GameObject avatarRoot, ConversionConfig config, System.Action<string, float> progressCallback = null)
        {
            ConversionSummary summary = new ConversionSummary();

            if (avatarRoot == null)
            {
                summary.AddError("Avatar root is null");
                return summary;
            }

            // Validate avatar has VRC_AvatarDescriptor
            if (!HasAvatarDescriptor(avatarRoot))
            {
                summary.AddError("Avatar root does not have VRC_AvatarDescriptor component", avatarRoot);
                return summary;
            }

            try
            {
                // Phase 1: Backup
                progressCallback?.Invoke("Creating backup...", 0.1f);
                string backupPath = null;
                if (!string.IsNullOrEmpty(config.backupLocation))
                {
                    // Check if BackupSystem is available, otherwise use BackupManager
                    backupPath = BackupSystemHelper.CreateBackup(avatarRoot, config.backupLocation, 
                        (msg, progress) => progressCallback?.Invoke(msg, 0.1f + progress * 0.05f));
                    
                    if (backupPath != null)
                    {
                        summary.AddSuccess($"Backup created: {backupPath}");
                    }
                    else
                    {
                        summary.AddWarning("Failed to create backup, continuing anyway...");
                    }
                }

                // Show confirmation dialog after backup, before modifications
                if (backupPath != null)
                {
                    progressCallback?.Invoke("Waiting for confirmation...", 0.15f);
                    
                    // Build summary of what will be modified
                    System.Text.StringBuilder changesSummary = new System.Text.StringBuilder();
                    changesSummary.AppendLine("Backup created successfully!");
                    changesSummary.AppendLine($"Location: {backupPath}");
                    changesSummary.AppendLine();
                    changesSummary.AppendLine("The following modifications will be made:");
                    
                    if (config.removeComponents)
                    {
                        // Count components that will be removed
                        Component[] allComponents = avatarRoot.GetComponentsInChildren<Component>(true);
                        int incompatibleCount = 0;
                        foreach (Component comp in allComponents)
                        {
                            if (comp != null && IsQuestIncompatibleComponent(comp))
                            {
                                incompatibleCount++;
                            }
                        }
                        changesSummary.AppendLine($"• Remove {incompatibleCount} incompatible component(s)");
                    }
                    
                    if (config.replaceShaders)
                    {
                        // Count materials that will be replaced
                        HashSet<Material> materials = new HashSet<Material>();
                        Renderer[] renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
                        foreach (Renderer renderer in renderers)
                        {
                            if (renderer != null)
                            {
                                foreach (Material mat in renderer.sharedMaterials)
                                {
                                    if (mat != null && mat.shader != null)
                                    {
                                        string shaderName = mat.shader.name;
                                        if (!shaderName.StartsWith("VRChat/Mobile/", StringComparison.OrdinalIgnoreCase))
                                        {
                                            materials.Add(mat);
                                        }
                                    }
                                }
                            }
                        }
                        changesSummary.AppendLine($"• Replace shaders on {materials.Count} material(s)");
                    }
                    
                    if (config.optimizeTextures)
                    {
                        changesSummary.AppendLine($"• Optimize textures (max size: {config.maxTextureSize}, quality: {config.compressionQuality})");
                    }
                    
                    changesSummary.AppendLine();
                    changesSummary.AppendLine("Proceed with modifications?");
                    
                    bool proceed = EditorUtility.DisplayDialog(
                        "Backup Complete - Confirm Modifications",
                        changesSummary.ToString(),
                        "Yes, Proceed",
                        "Cancel"
                    );
                    
                    if (!proceed)
                    {
                        summary.AddWarning("Conversion cancelled by user after backup creation.");
                        progressCallback?.Invoke("Conversion cancelled.", 1.0f);
                        return summary;
                    }
                }

                // Phase 2: Remove incompatible components
                if (config.removeComponents)
                {
                    progressCallback?.Invoke("Removing incompatible components...", 0.2f);
                    var removedComponents = QuestComponentRemover.RemoveIncompatibleComponents(
                        avatarRoot,
                        (msg) => progressCallback?.Invoke(msg, 0.3f)
                    );

                    summary.componentsRemoved = removedComponents.Count;
                    foreach (var removed in removedComponents)
                    {
                        summary.AddSuccess($"Removed {removed.componentType} from {removed.gameObjectPath}", removed.gameObject);
                    }
                }

                // Phase 3: Replace shaders
                if (config.replaceShaders)
                {
                    progressCallback?.Invoke("Replacing shaders...", 0.4f);
                    ReplaceShaders(avatarRoot, summary, (msg, progress) => progressCallback?.Invoke(msg, 0.4f + progress * 0.3f));
                }

                // Phase 4: Optimize textures
                if (config.optimizeTextures)
                {
                    progressCallback?.Invoke("Optimizing textures...", 0.7f);
                    var optimizedTextures = TextureOptimizer.OptimizeTextures(
                        avatarRoot,
                        config.maxTextureSize,
                        config.compressionQuality,
                        config.useCrunchCompression,
                        (msg) => progressCallback?.Invoke(msg, 0.8f)
                    );

                    summary.texturesOptimized = optimizedTextures.Count;
                    foreach (var opt in optimizedTextures)
                    {
                        summary.AddSuccess($"Optimized texture: {opt.texturePath}");
                    }
                }

                // Phase 5: Additional optimizations
                progressCallback?.Invoke("Applying optimizations...", 0.9f);
                EnableGPUInstancing(avatarRoot, summary);

                // Save all changes
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                progressCallback?.Invoke("Conversion complete!", 1.0f);
            }
            catch (Exception e)
            {
                summary.AddError($"Conversion failed: {e.Message}\n{e.StackTrace}");
                Debug.LogError($"VRC-QuestPatcher conversion error: {e}");
            }

            return summary;
        }

        /// <summary>
        /// Checks if the GameObject has a VRC_AvatarDescriptor component
        /// </summary>
        private static bool HasAvatarDescriptor(GameObject obj)
        {
            if (obj == null) return false;

            // Try to find VRC_AvatarDescriptor using reflection (since it's from VRChat SDK)
            Component[] components = obj.GetComponents<Component>();
            foreach (Component comp in components)
            {
                if (comp == null) continue;
                string typeName = comp.GetType().FullName;
                if (typeName.Contains("VRC_AvatarDescriptor") || typeName.Contains("VRCAvatarDescriptor"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Replaces all shaders in the avatar with Quest-compatible alternatives
        /// </summary>
        private static void ReplaceShaders(GameObject avatarRoot, ConversionSummary summary, System.Action<string, float> progressCallback = null)
        {
            HashSet<Material> processedMaterials = new HashSet<Material>();
            List<Material> allMaterials = new List<Material>();

            // Collect all materials
            Renderer[] renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;

                Material[] materials = renderer.sharedMaterials;
                foreach (Material mat in materials)
                {
                    if (mat != null && !processedMaterials.Contains(mat))
                    {
                        processedMaterials.Add(mat);
                        allMaterials.Add(mat);
                    }
                }
            }

            int total = allMaterials.Count;
            for (int i = 0; i < allMaterials.Count; i++)
            {
                Material mat = allMaterials[i];
                progressCallback?.Invoke($"Replacing shaders ({i + 1}/{total})...", (float)i / total);

                try
                {
                    if (mat == null || mat.shader == null)
                        continue;

                    string originalShaderName = mat.shader.name;

                    // Check if already Quest-compatible
                    if (originalShaderName.StartsWith("VRChat/Mobile/", StringComparison.OrdinalIgnoreCase))
                    {
                        summary.materialsSkipped++;
                        continue;
                    }

                    // Find replacement
                    var replacement = ShaderMapping.FindReplacementShader(originalShaderName);

                    if (replacement.Success && replacement.ReplacementShader != null)
                    {
                        Undo.RegisterCompleteObjectUndo(mat, "Replace shader for Quest compatibility");
                        
                        // Create a temporary material copy to preserve properties before shader replacement
                        Material tempMaterial = new Material(mat);
                        
                        // Replace shader (this clears properties)
                        mat.shader = replacement.ReplacementShader;
                        
                        // Transfer compatible properties from the temporary copy
                        var propertyTransfer = ShaderPropertyMapper.TransferProperties(
                            tempMaterial, // Source material with original shader
                            mat, // Target material with new shader
                            replacement.ReplacementShader
                        );
                        
                        // Clean up temporary material
                        UnityEngine.Object.DestroyImmediate(tempMaterial);
                        
                        if (propertyTransfer.PropertiesTransferred > 0)
                        {
                            Debug.Log($"Transferred {propertyTransfer.PropertiesTransferred} properties from {originalShaderName} to {replacement.ReplacementShader.name} on material {mat.name}");
                        }
                        
                        EditorUtility.SetDirty(mat);

                        // Enable GPU Instancing if supported
                        if (replacement.ReplacementShader.name.Contains("Mobile"))
                        {
                            mat.enableInstancing = true;
                        }

                        summary.materialsReplaced++;
                        string materialPath = AssetDatabase.GetAssetPath(mat);
                        string transferInfo = propertyTransfer.PropertiesTransferred > 0 
                            ? $" ({propertyTransfer.PropertiesTransferred} properties transferred)" 
                            : "";
                        summary.AddSuccess($"Replaced shader: {originalShaderName} → {replacement.ReplacementShader.name} ({replacement.MatchType}){transferInfo}", mat, materialPath);
                    }
                    else if (replacement.IsAlreadyCompatible)
                    {
                        summary.materialsSkipped++;
                    }
                    else
                    {
                        summary.materialsFailed++;
                        string materialPath = AssetDatabase.GetAssetPath(mat);
                        summary.AddError($"Could not find Quest replacement for shader: {originalShaderName}", mat, materialPath);
                    }
                }
                catch (Exception e)
                {
                    summary.materialsFailed++;
                    string materialPath = AssetDatabase.GetAssetPath(mat);
                    summary.AddError($"Error replacing shader in material: {e.Message}", mat, materialPath);
                }
            }
        }

        /// <summary>
        /// Checks if a component is Quest-incompatible (same logic as BackupManager)
        /// </summary>
        private static bool IsQuestIncompatibleComponent(Component comp)
        {
            if (comp == null) return false;
            
            string typeName = comp.GetType().FullName;
            string typeNameLower = typeName.ToLowerInvariant();
            
            return typeNameLower.Contains("dynamicbone") ||
                   comp is Cloth ||
                   comp is Camera ||
                   comp is Light ||
                   comp is AudioSource ||
                   comp is Rigidbody ||
                   comp is Collider ||
                   comp is Joint ||
                   comp is ParticleSystem ||
                   typeNameLower.Contains("constraint") ||
                   typeNameLower.Contains("finalik");
        }

        /// <summary>
        /// Enables GPU Instancing on all materials
        /// </summary>
        private static void EnableGPUInstancing(GameObject avatarRoot, ConversionSummary summary)
        {
            HashSet<Material> processedMaterials = new HashSet<Material>();

            Renderer[] renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;

                Material[] materials = renderer.sharedMaterials;
                foreach (Material mat in materials)
                {
                    if (mat != null && !processedMaterials.Contains(mat))
                    {
                        processedMaterials.Add(mat);

                        try
                        {
                            if (mat.shader != null && mat.shader.name.StartsWith("VRChat/Mobile/", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!mat.enableInstancing)
                                {
                                    Undo.RegisterCompleteObjectUndo(mat, "Enable GPU Instancing");
                                    mat.enableInstancing = true;
                                    EditorUtility.SetDirty(mat);
                                    summary.gpuInstancingEnabled++;
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"Failed to enable GPU instancing on material {mat.name}: {e.Message}");
                        }
                    }
                }
            }
        }
    }
}
