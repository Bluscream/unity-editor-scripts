using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class MissingShaderReplacer : EditorWindow
{
    [MenuItem("Window/Missing Shader Replacer")]
    public static void ShowWindow()
    {
        GetWindow<MissingShaderReplacer>("Missing Shader Replacer");
    }

    private void OnGUI()
    {
        if (GUILayout.Button("Find and Replace Missing Shaders"))
        {
            FindAndReplaceMissingShaders();
        }
    }

    private void FindAndReplaceMissingShaders()
    {
        try
        {
            Debug.Log("Starting shader replacement process...");
            var materials = AssetDatabase.FindAssets("t:Material", null);

            Debug.Log($"Found {materials.Length} materials");

            int totalMaterials = materials.Length;
            int processedCount = 0;
            int errorCount = 0;

            foreach (var guid in materials)
            {
                try
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path))
                        continue;

                    var asset = AssetDatabase.LoadMainAssetAtPath(path);
                    if (asset == null || asset.GetType() != typeof(Material))
                        continue;
                        
                    var material = asset as Material;
                    if (material != null && material.shader != null && !string.IsNullOrEmpty(material.shader.name))
                    {
                        Shader originalShader = material.shader;
                        string shaderPath = "Assets/Shader/" + originalShader.name;
                        
                        // Check if shader exists
                        var shaderAsset = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                        if (shaderAsset == null)
                        {
                            // Try alternative path
                            shaderAsset = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath + ".shader");
                        }
                        
                        if (shaderAsset == null)
                        {
                            Shader standardShader = Shader.Find("Standard");
                            if (standardShader != null)
                            {
                                Debug.Log($"Replacing missing shader: {originalShader.name} with Standard");
                                material.shader = standardShader;
                                EditorUtility.SetDirty(material);
                                processedCount++;
                            }
                            else
                            {
                                Debug.LogWarning($"Could not find Standard shader to replace {originalShader.name}");
                                errorCount++;
                            }
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Error processing material with GUID {guid}: {e.Message}");
                    errorCount++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Processed {processedCount} materials out of {totalMaterials} (Errors: {errorCount})");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error during shader replacement: {e.Message}\n{e.StackTrace}");
        }
    }
}
