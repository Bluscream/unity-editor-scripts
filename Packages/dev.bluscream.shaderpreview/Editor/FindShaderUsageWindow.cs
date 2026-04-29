using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static Bluscream.Utils;

namespace Bluscream.ShaderTest
{
    /// <summary>
    /// Window for finding materials that use a specific shader within a GameObject hierarchy
    /// </summary>
    public class FindShaderUsageWindow : EditorWindow
    {
        private GameObject rootGameObject;
        private Shader targetShader;
        private string shaderSearchText = "";
        private Vector2 scrollPosition;
        private List<MaterialInfo> foundMaterials = new List<MaterialInfo>();
        private bool hasSearched = false;
        
        // Shader replacement
        private List<Shader> allShaders = new List<Shader>();
        private string[] shaderNames = new string[0];
        private int selectedShaderIndex = 0;
        private bool shadersLoaded = false;

        [System.Serializable]
        private class MaterialInfo
        {
            public Material material;
            public GameObject gameObject;
            public Renderer renderer;
            public int materialIndex;
            public string materialPath;
            public string gameObjectPath;
        }

        [MenuItem("Bluscream/Shader Preview/Find Shader Usage")]
        public static void ShowWindow()
        {
            FindShaderUsageWindow window = GetWindow<FindShaderUsageWindow>("Find Shader Usage");
            window.minSize = new Vector2(500, 400);
        }

        private void OnEnable()
        {
            if (!shadersLoaded)
            {
                LoadAllShaders();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Find Shader Usage", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Root GameObject field
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Avatar Root (Optional)", EditorStyles.boldLabel);
            
            GameObject newRoot = (GameObject)EditorGUILayout.ObjectField(
                rootGameObject,
                typeof(GameObject),
                true,
                GUILayout.Height(20)
            );

            if (newRoot != rootGameObject)
            {
                rootGameObject = newRoot;
                hasSearched = false;
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Shader search section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Shader to Find", EditorStyles.boldLabel);

            // Shader drag and drop
            Shader newShader = (Shader)EditorGUILayout.ObjectField(
                targetShader,
                typeof(Shader),
                false,
                GUILayout.Height(20)
            );

            if (newShader != targetShader)
            {
                targetShader = newShader;
                if (targetShader != null)
                {
                    shaderSearchText = targetShader.name;
                }
                hasSearched = false;
            }

            EditorGUILayout.Space(3);

            // Shader name/path search (case-insensitive, glob pattern)
            EditorGUILayout.LabelField("Or search by shader name or path (case-insensitive, glob pattern, e.g., *Mobile*, VRChat/*, \"Standard\" for exact match):");
            string newSearchText = EditorGUILayout.TextField(shaderSearchText);

            if (newSearchText != shaderSearchText)
            {
                shaderSearchText = newSearchText;
                targetShader = null; // Clear direct shader selection when typing
                hasSearched = false;
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Search button
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(shaderSearchText) && targetShader == null);
            if (GUILayout.Button("Search", GUILayout.Height(30)))
            {
                SearchForShader();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(5);

            // Results
            if (hasSearched)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Found {foundMaterials.Count} material(s)", EditorStyles.boldLabel);

                if (foundMaterials.Count == 0)
                {
                    EditorGUILayout.HelpBox("No materials found using the specified shader.", MessageType.Info);
                }
                else
                {
                    // Shader replacement section
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Replace Shaders", EditorStyles.boldLabel);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Replacement Shader:", GUILayout.Width(140));
                    
                    if (shaderNames.Length > 0)
                    {
                        int newIndex = EditorGUILayout.Popup(selectedShaderIndex, shaderNames);
                        if (newIndex != selectedShaderIndex)
                        {
                            selectedShaderIndex = newIndex;
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField("No shaders available", EditorStyles.helpBox);
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.Space(5);
                    
                    EditorGUI.BeginDisabledGroup(shaderNames.Length == 0 || selectedShaderIndex < 0 || selectedShaderIndex >= allShaders.Count);
                    if (GUILayout.Button($"Replace All {foundMaterials.Count} Material Shaders", GUILayout.Height(30)))
                    {
                        ReplaceAllShaders();
                    }
                    EditorGUI.EndDisabledGroup();
                    
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(5);
                    
                    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

                    foreach (MaterialInfo info in foundMaterials)
                    {
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        
                        // Material name
                        EditorGUILayout.LabelField("Material:", info.material.name, EditorStyles.boldLabel);

                        // Current shader
                        if (info.material != null && info.material.shader != null)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("Shader:", info.material.shader.name, EditorStyles.boldLabel);
                            if (GUILayout.Button("...", GUILayout.Width(30)))
                            {
                                EditorGUIUtility.PingObject(info.material.shader);
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                        else
                        {
                            EditorGUILayout.LabelField("Shader:", "(None)", EditorStyles.miniLabel);
                        }

                        // Material path
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("Material Path:", info.materialPath);
                        if (GUILayout.Button("...", GUILayout.Width(30)))
                        {
                            EditorGUIUtility.PingObject(info.material);
                        }
                        EditorGUILayout.EndHorizontal();
                        
                        // GameObject path
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("GameObject Path:", info.gameObjectPath);
                        if (GUILayout.Button("...", GUILayout.Width(30)))
                        {
                            EditorGUIUtility.PingObject(info.gameObject);
                            Selection.activeGameObject = info.gameObject;
                        }
                        EditorGUILayout.EndHorizontal();
                        
                        // Material index (if multiple materials on renderer)
                        if (info.materialIndex >= 0)
                        {
                            EditorGUILayout.LabelField("Material Index:", info.materialIndex.ToString());
                        }

                        EditorGUILayout.EndVertical();
                        EditorGUILayout.Space(2);
                    }

                    EditorGUILayout.EndScrollView();
                }

                EditorGUILayout.EndVertical();
            }
        }

        private void SearchForShader()
        {
            foundMaterials.Clear();
            hasSearched = true;

            // Collect all matching shaders
            HashSet<Shader> matchingShaders = new HashSet<Shader>();

            if (targetShader != null)
            {
                // Direct shader selection
                matchingShaders.Add(targetShader);
            }
            else if (!string.IsNullOrEmpty(shaderSearchText))
            {
                // Check if search text is in quotes (exact match)
                bool isExactMatch = Utils.IsQuotedPattern(shaderSearchText);
                string searchPattern = isExactMatch ? Utils.UnquotePattern(shaderSearchText) : shaderSearchText;
                
                // First, try to find built-in shaders using Shader.Find()
                // This handles Unity's built-in shaders like "Standard", "Unlit/Color", etc.
                // Shader.Find() only works with exact shader names, so we try it for exact matches
                // or when the pattern doesn't contain wildcards
                if (isExactMatch || (!searchPattern.Contains("*") && !searchPattern.Contains("?")))
                {
                    Shader builtInShader = Shader.Find(searchPattern);
                    if (builtInShader != null)
                    {
                        // For exact matches, verify it matches exactly
                        // For non-wildcard patterns, check if it matches
                        bool nameMatches = isExactMatch 
                            ? string.Equals(builtInShader.name, searchPattern, StringComparison.OrdinalIgnoreCase)
                            : Utils.GlobMatch(builtInShader.name, searchPattern);
                        
                        if (nameMatches)
                        {
                            matchingShaders.Add(builtInShader);
                        }
                    }
                }
                
                // Also search project shaders using AssetDatabase
                string[] shaderGuids = AssetDatabase.FindAssets("t:Shader");
                
                foreach (string guid in shaderGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                    
                    if (shader != null && !matchingShaders.Contains(shader))
                    {
                        // Check shader name (case-insensitive, glob pattern or exact match)
                        bool nameMatches = isExactMatch 
                            ? string.Equals(shader.name, searchPattern, StringComparison.OrdinalIgnoreCase)
                            : Utils.GlobMatch(shader.name, searchPattern);
                        
                        // Check shader path (case-insensitive, glob pattern or exact match)
                        bool pathMatches = isExactMatch
                            ? string.Equals(path, searchPattern, StringComparison.OrdinalIgnoreCase)
                            : Utils.GlobMatch(path, searchPattern);
                        
                        if (nameMatches || pathMatches)
                        {
                            matchingShaders.Add(shader);
                        }
                    }
                }
            }

            if (matchingShaders.Count == 0)
            {
                EditorUtility.DisplayDialog("Shader Not Found", $"Could not find any shaders matching '{shaderSearchText}'.", "OK");
                return;
            }

            // Collect materials using any of the matching shaders
            HashSet<Material> processedMaterials = new HashSet<Material>();

            if (rootGameObject != null)
            {
                // Search within root GameObject hierarchy
                Renderer[] renderers = rootGameObject.GetComponentsInChildren<Renderer>(true);
                
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null) continue;

                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        Material mat = materials[i];
                        if (mat != null && matchingShaders.Contains(mat.shader) && !processedMaterials.Contains(mat))
                        {
                            processedMaterials.Add(mat);
                            
                            string materialPath = AssetDatabase.GetAssetPath(mat);
                            if (string.IsNullOrEmpty(materialPath))
                            {
                                materialPath = "(Scene Material)";
                            }

                            foundMaterials.Add(new MaterialInfo
                            {
                                material = mat,
                                gameObject = renderer.gameObject,
                                renderer = renderer,
                                materialIndex = materials.Length > 1 ? i : -1,
                                materialPath = materialPath,
                                gameObjectPath = Utils.GetGameObjectPath(renderer.gameObject)
                            });
                        }
                    }
                }
            }
            else
            {
                // Search all materials in project
                string[] materialGuids = AssetDatabase.FindAssets("t:Material");
                
                foreach (string guid in materialGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    
                    if (mat != null && matchingShaders.Contains(mat.shader) && !processedMaterials.Contains(mat))
                    {
                        processedMaterials.Add(mat);
                        
                        foundMaterials.Add(new MaterialInfo
                        {
                            material = mat,
                            gameObject = null,
                            renderer = null,
                            materialIndex = -1,
                            materialPath = path,
                            gameObjectPath = "(Asset Material)"
                        });
                    }
                }
            }

            // Sort by material name
            foundMaterials = foundMaterials.OrderBy(m => m.material.name).ToList();
        }

        /// <summary>
        /// Loads all shaders (built-in and project shaders) for the dropdown
        /// </summary>
        private void LoadAllShaders()
        {
            allShaders = Utils.GetAllShaders();
            
            // Create display names array
            shaderNames = allShaders.Select(s => s.name).ToArray();
            
            shadersLoaded = true;
        }

        /// <summary>
        /// Replaces all found materials' shaders with the selected replacement shader
        /// </summary>
        private void ReplaceAllShaders()
        {
            if (selectedShaderIndex < 0 || selectedShaderIndex >= allShaders.Count)
            {
                EditorUtility.DisplayDialog("Error", "Please select a valid replacement shader.", "OK");
                return;
            }

            Shader replacementShader = allShaders[selectedShaderIndex];
            if (replacementShader == null)
            {
                EditorUtility.DisplayDialog("Error", "Selected replacement shader is null.", "OK");
                return;
            }

            if (foundMaterials.Count == 0)
            {
                EditorUtility.DisplayDialog("No Materials", "No materials found to replace.", "OK");
                return;
            }

            // Confirm replacement
            bool confirmed = EditorUtility.DisplayDialog(
                "Replace Shaders",
                $"Replace shaders in {foundMaterials.Count} material(s) with '{replacementShader.name}'?\n\n" +
                "This action cannot be undone.",
                "Replace",
                "Cancel"
            );

            if (!confirmed)
                return;

            int replacedCount = 0;
            int errorCount = 0;
            HashSet<Material> processedMaterials = new HashSet<Material>();

            try
            {
                foreach (MaterialInfo info in foundMaterials)
                {
                    if (info.material == null)
                        continue;

                    // Skip if already processed (same material can appear multiple times)
                    if (processedMaterials.Contains(info.material))
                        continue;

                    processedMaterials.Add(info.material);

                    try
                    {
                        // Register undo for the material
                        Undo.RegisterCompleteObjectUndo(info.material, "Replace Shader");

                        // Replace shader
                        info.material.shader = replacementShader;

                        // Mark material as dirty
                        EditorUtility.SetDirty(info.material);

                        replacedCount++;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error replacing shader in material '{info.material.name}': {e.Message}");
                        errorCount++;
                    }
                }

                // Also handle renderer materials if they're scene materials
                foreach (MaterialInfo info in foundMaterials)
                {
                    if (info.renderer != null && info.materialIndex >= 0)
                    {
                        try
                        {
                            Material[] materials = info.renderer.sharedMaterials;
                            if (info.materialIndex < materials.Length && materials[info.materialIndex] != null)
                            {
                                // The material should already be replaced, but update the renderer
                                Undo.RegisterCompleteObjectUndo(info.renderer, "Replace Shader");
                                EditorUtility.SetDirty(info.renderer);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"Error updating renderer '{info.renderer.name}': {e.Message}");
                        }
                    }
                }

                // Refresh asset database
                AssetDatabase.Refresh();

                string message = $"Replaced shaders in {replacedCount} material(s) with '{replacementShader.name}'.";
                if (errorCount > 0)
                {
                    message += $"\n\n{errorCount} error(s) occurred. Check console for details.";
                }

                EditorUtility.DisplayDialog("Replace Complete", message, "OK");
                Debug.Log($"Shader replacement complete: {replacedCount} materials replaced, {errorCount} errors");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Error during shader replacement: {e.Message}", "OK");
                Debug.LogError($"Shader replacement error: {e}\n{e.StackTrace}");
            }
        }

    }
}
