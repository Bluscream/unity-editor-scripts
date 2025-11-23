using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

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
                // Search by name or partial path
                string[] shaderGuids = AssetDatabase.FindAssets("t:Shader");
                
                foreach (string guid in shaderGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                    
                    if (shader != null)
                    {
                        // Check if search text is in quotes (exact match)
                        bool isExactMatch = IsQuotedPattern(shaderSearchText);
                        string searchPattern = isExactMatch ? UnquotePattern(shaderSearchText) : shaderSearchText;
                        
                        // Check shader name (case-insensitive, glob pattern or exact match)
                        bool nameMatches = isExactMatch 
                            ? string.Equals(shader.name, searchPattern, StringComparison.OrdinalIgnoreCase)
                            : GlobMatch(shader.name, searchPattern);
                        
                        // Check shader path (case-insensitive, glob pattern or exact match)
                        bool pathMatches = isExactMatch
                            ? string.Equals(path, searchPattern, StringComparison.OrdinalIgnoreCase)
                            : GlobMatch(path, searchPattern);
                        
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
                                gameObjectPath = GetGameObjectPath(renderer.gameObject)
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

        private string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return "";
            
            string path = obj.name;
            Transform current = obj.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        /// <summary>
        /// Checks if a pattern is in quotes (exact match)
        /// </summary>
        private bool IsQuotedPattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;

            pattern = pattern.Trim();
            return (pattern.StartsWith("\"") && pattern.EndsWith("\"")) ||
                   (pattern.StartsWith("'") && pattern.EndsWith("'"));
        }

        /// <summary>
        /// Removes quotes from a pattern
        /// </summary>
        private string UnquotePattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return pattern;

            pattern = pattern.Trim();
            if ((pattern.StartsWith("\"") && pattern.EndsWith("\"")) ||
                (pattern.StartsWith("'") && pattern.EndsWith("'")))
            {
                return pattern.Substring(1, pattern.Length - 2);
            }
            return pattern;
        }

        /// <summary>
        /// Matches a string against a glob pattern (case-insensitive)
        /// Supports * (any sequence) and ? (single character)
        /// </summary>
        private bool GlobMatch(string input, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;

            if (string.IsNullOrEmpty(input))
                return false;

            // Convert glob pattern to regex
            // Escape special regex characters except * and ?
            string regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")  // * matches any sequence
                .Replace("\\?", ".")   // ? matches single character
                + "$";

            try
            {
                return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
            }
            catch
            {
                // If regex fails, fall back to simple case-insensitive contains
                return input.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
    }
}
