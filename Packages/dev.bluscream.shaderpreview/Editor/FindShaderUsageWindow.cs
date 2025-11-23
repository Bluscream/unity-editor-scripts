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

        [MenuItem("Tools/Find Shader Usage")]
        public static void ShowWindow()
        {
            FindShaderUsageWindow window = GetWindow<FindShaderUsageWindow>("Find Shader Usage");
            window.minSize = new Vector2(500, 400);
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
                    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

                    foreach (MaterialInfo info in foundMaterials)
                    {
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        
                        EditorGUILayout.BeginHorizontal();
                        
                        // Material name
                        EditorGUILayout.LabelField("Material:", info.material.name, EditorStyles.boldLabel);
                        
                        // Ping material button
                        if (GUILayout.Button("Ping Material", GUILayout.Width(100)))
                        {
                            EditorGUIUtility.PingObject(info.material);
                        }

                        // Ping GameObject button
                        if (GUILayout.Button("Ping GameObject", GUILayout.Width(120)))
                        {
                            EditorGUIUtility.PingObject(info.gameObject);
                            Selection.activeGameObject = info.gameObject;
                        }

                        EditorGUILayout.EndHorizontal();

                        // Material path
                        EditorGUILayout.LabelField("Material Path:", info.materialPath);
                        
                        // GameObject path
                        EditorGUILayout.LabelField("GameObject Path:", info.gameObjectPath);
                        
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

    }
}
