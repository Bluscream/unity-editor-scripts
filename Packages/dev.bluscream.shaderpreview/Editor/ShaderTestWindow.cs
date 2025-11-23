using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bluscream.ShaderTest
{
    /// <summary>
    /// Editor window for quickly testing different shaders on a material
    /// </summary>
    public class ShaderTestWindow : EditorWindow
    {
        private Material targetMaterial;
        private Shader originalShader;
        private Shader currentShader;
        private Vector2 scrollPosition;
        private Dictionary<string, List<Shader>> shadersByPath = new Dictionary<string, List<Shader>>();
        private bool shadersLoaded = false;

        [MenuItem("Tools/Shader Test")]
        public static void ShowWindow()
        {
            ShaderTestWindow window = GetWindow<ShaderTestWindow>("Shader Test");
            window.minSize = new Vector2(400, 300);
        }

        private void OnEnable()
        {
            // Load shaders automatically when window opens
            if (!shadersLoaded)
            {
                LoadShaders();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Shader Test", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Material drag and drop field with inline reset button
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Material", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            Material newMaterial = (Material)EditorGUILayout.ObjectField(
                targetMaterial,
                typeof(Material),
                false,
                GUILayout.Height(20)
            );

            // Reset button inline
            EditorGUI.BeginDisabledGroup(targetMaterial == null || currentShader == originalShader);
            if (GUILayout.Button("Reset Shader", GUILayout.Width(100), GUILayout.Height(20)))
            {
                ResetShader();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (newMaterial != targetMaterial)
            {
                if (targetMaterial != null && originalShader != null)
                {
                    // Restore original shader before switching materials
                    targetMaterial.shader = originalShader;
                }

                targetMaterial = newMaterial;
                if (targetMaterial != null)
                {
                    originalShader = targetMaterial.shader;
                    currentShader = originalShader;
                }
                else
                {
                    originalShader = null;
                    currentShader = null;
                }
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Shader buttons
            if (targetMaterial == null)
            {
                EditorGUILayout.HelpBox("Drag a material into the field above to start testing shaders.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Available Shaders", EditorStyles.boldLabel);

            if (shadersByPath.Count == 0)
            {
                EditorGUILayout.HelpBox("No shaders found in the project.", MessageType.Warning);
            }
            else
            {
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

                // Group shaders by path, with Hidden shaders at the end
                const string hiddenGroupName = "Hidden";
                var sortedGroups = shadersByPath.OrderBy(kvp => 
                    kvp.Key == hiddenGroupName ? 1 : 0  // Hidden group goes to end (1)
                ).ThenBy(kvp => kvp.Key);  // Then sort alphabetically within each group
                
                foreach (var pathGroup in sortedGroups)
                {
                    EditorGUILayout.Space(3);
                    
                    // Path header
                    EditorGUILayout.LabelField(pathGroup.Key, EditorStyles.miniLabel);
                    
                    // Shader buttons in this path
                    EditorGUI.indentLevel++;
                    foreach (Shader shader in pathGroup.Value.OrderBy(s => s.name))
                    {
                        bool isCurrent = currentShader == shader;
                        bool isOriginal = originalShader == shader;
                        
                        // Create button with different styling
                        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
                        if (isCurrent)
                        {
                            buttonStyle.normal.background = Texture2D.whiteTexture;
                            buttonStyle.normal.textColor = Color.black;
                        }
                        else if (isOriginal)
                        {
                            buttonStyle.normal.textColor = Color.green;
                        }

                        EditorGUILayout.BeginHorizontal();
                        
                        string buttonText = shader.name;
                        if (isCurrent)
                            buttonText = "✓ " + buttonText;
                        if (isOriginal)
                            buttonText += " (Original)";

                        if (GUILayout.Button(buttonText, buttonStyle))
                        {
                            ApplyShader(shader);
                        }

                        // Ping button
                        if (GUILayout.Button("...", GUILayout.Width(30)))
                        {
                            EditorGUIUtility.PingObject(shader);
                        }

                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        private void LoadShaders()
        {
            shadersByPath.Clear();
            
            // Find all shaders in the project
            string[] shaderGuids = AssetDatabase.FindAssets("t:Shader");
            
            const string hiddenGroupName = "Hidden";
            
            foreach (string guid in shaderGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                
                if (shader != null)
                {
                    // Check if this is a Hidden shader
                    bool isHidden = shader.name.StartsWith("Hidden/");
                    
                    string folderPath;
                    if (isHidden)
                    {
                        // Group all Hidden shaders together
                        folderPath = hiddenGroupName;
                    }
                    else
                    {
                        // Extract folder path for non-hidden shaders
                        int lastSlash = path.LastIndexOf('/');
                        folderPath = lastSlash >= 0 ? path.Substring(0, lastSlash) : "Root";
                        
                        // Remove "Assets/" prefix for cleaner display
                        if (folderPath.StartsWith("Assets/"))
                        {
                            folderPath = folderPath.Substring(7);
                        }
                    }
                    
                    if (!shadersByPath.ContainsKey(folderPath))
                    {
                        shadersByPath[folderPath] = new List<Shader>();
                    }
                    
                    shadersByPath[folderPath].Add(shader);
                }
            }
            
            shadersLoaded = true;
        }

        private void ApplyShader(Shader shader)
        {
            if (targetMaterial == null || shader == null)
                return;

            // Store current shader
            currentShader = shader;
            
            // Apply shader to material
            targetMaterial.shader = shader;
            
            // Mark material as dirty so changes are saved if user wants
            EditorUtility.SetDirty(targetMaterial);
        }

        private void ResetShader()
        {
            if (targetMaterial == null || originalShader == null)
                return;

            currentShader = originalShader;
            targetMaterial.shader = originalShader;
            EditorUtility.SetDirty(targetMaterial);
        }

        private void OnDestroy()
        {
            // Restore original shader when window is closed
            if (targetMaterial != null && originalShader != null)
            {
                targetMaterial.shader = originalShader;
            }
        }
    }
}
