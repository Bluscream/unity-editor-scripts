using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static Bluscream.Utils;

namespace Bluscream.ShaderTest
{
    /// <summary>
    /// Editor window for quickly testing different shaders on a material
    /// </summary>
    public class ShaderTestWindow : EditorWindow
    {
        private Material targetMaterial;
        private GameObject targetGameObject;
        private Shader originalShader;
        private Shader currentShader;
        private Vector2 scrollPosition;
        private Dictionary<string, List<Shader>> shadersByPath = new Dictionary<string, List<Shader>>();
        private bool shadersLoaded = false;

        [MenuItem("Bluscream/Shader Preview/Shader Test")]
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
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Shader Test", EditorStyles.boldLabel);
            if (GUILayout.Button("Print to Log", GUILayout.Width(100), GUILayout.Height(20)))
            {
                PrintAllShadersToLog();
            }
            if (GUILayout.Button("Reload Shaders", GUILayout.Width(110), GUILayout.Height(20)))
            {
                LoadShaders();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);

            // Material/GameObject drag and drop field with inline reset button
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Material / GameObject", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            
            // Accept either Material or GameObject (or any UnityEngine.Object with a Renderer/Material)
            UnityEngine.Object droppedObj = EditorGUILayout.ObjectField(
                (UnityEngine.Object)targetMaterial ?? targetGameObject,
                typeof(UnityEngine.Object),
                true,
                GUILayout.Height(20)
            );

            Material resolvedMat = null;
            GameObject resolvedGO = null;

            if (droppedObj is Material mat)
            {
                resolvedMat = mat;
            }
            else if (droppedObj is GameObject go)
            {
                resolvedGO = go;
                Renderer r = go.GetComponentInChildren<Renderer>(true);
                if (r != null && r.sharedMaterial != null)
                {
                    resolvedMat = r.sharedMaterial;
                }
            }
            else if (droppedObj is Component comp)
            {
                resolvedGO = comp.gameObject;
                Renderer r = comp.GetComponentInChildren<Renderer>(true);
                if (r != null && r.sharedMaterial != null)
                {
                    resolvedMat = r.sharedMaterial;
                }
            }

            // Reset button inline
            EditorGUI.BeginDisabledGroup(targetMaterial == null || currentShader == originalShader);
            if (GUILayout.Button("Reset", GUILayout.Width(60), GUILayout.Height(20)))
            {
                ResetShader();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (resolvedMat != targetMaterial)
            {
                if (targetMaterial != null && originalShader != null)
                {
                    // Restore original shader before switching materials
                    targetMaterial.shader = originalShader;
                }

                targetMaterial = resolvedMat;
                targetGameObject = resolvedGO;
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
                EditorGUILayout.HelpBox("Drag a Material or GameObject into the field above to start testing shaders.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Available Shaders", EditorStyles.boldLabel);

            // Auto-reload shaders if list became empty (e.g. after domain reload / package recompile)
            if (shadersByPath == null || shadersByPath.Count == 0)
            {
                LoadShaders();
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
            shadersByPath = Utils.GetShadersByPath();
            shadersLoaded = true;
        }

        private void PrintAllShadersToLog()
        {
            LoadShaders();
            int totalShaders = shadersByPath.Values.Sum(list => list.Count);
            
            string goPath = targetGameObject != null ? AnimationUtility.CalculateTransformPath(targetGameObject.transform, null) : null;
            if (string.IsNullOrEmpty(goPath) && targetGameObject != null) goPath = targetGameObject.name;
            string matPath = targetMaterial != null ? AssetDatabase.GetAssetPath(targetMaterial) : null;
            if (string.IsNullOrEmpty(matPath) && targetMaterial != null) matPath = targetMaterial.name;

            Debug.Log($"<color=cyan><b>================================================================================</b></color>");
            Debug.Log($"<color=cyan><b>[ShaderTest] Shader Dump ({totalShaders} shaders across {shadersByPath.Count} categories):</b></color>");
            if (!string.IsNullOrEmpty(goPath))
                Debug.Log($"  <b>GameObject:</b> {goPath}");
            if (!string.IsNullOrEmpty(matPath))
                Debug.Log($"  <b>Material:</b> {matPath}");
            if (targetMaterial != null)
                Debug.Log($"  <b>Active Shader:</b> {targetMaterial.shader?.name} (Original: {originalShader?.name})");

            foreach (var kvp in shadersByPath.OrderBy(k => k.Key))
            {
                Debug.Log($"<color=lime><b>[Category: {kvp.Key}]</b></color> ({kvp.Value.Count} shaders)");
                foreach (Shader s in kvp.Value.OrderBy(s => s.name))
                {
                    string suffix = "";
                    if (s == originalShader) suffix += " <color=yellow><b>(Original)</b></color>";
                    if (s == currentShader) suffix += " <color=green><b>(Selected)</b></color>";

                    Debug.Log($"  • {s.name}{suffix}");
                }
            }
            Debug.Log($"<color=cyan><b>================================================================================</b></color>");
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
