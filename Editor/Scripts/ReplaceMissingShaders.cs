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
            var materialPath = "/Assets/Material";
            var materials = AssetDatabase.FindAssets("t:material", null);

            Debug.Log($"Found {materials.Length} materials in {materialPath}");

            int totalMaterials = materials.Length;
            int processedCount = 0;

            foreach (var guid in materials)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset.GetType() != typeof(Material))
                    continue;
                var material = asset as Material;
                if (material != null && !string.IsNullOrEmpty(material.shader.name))
                {
                    Shader originalShader = material.shader;
                    if (
                        AssetDatabase.GetMainAssetTypeAtPath("Assets/Shader/" + originalShader.name)
                        == null
                    )
                    {
                        Debug.Log($"Replacing missing shader: {originalShader.name}");
                        material.shader = Shader.Find("Standard");

                        processedCount++;
                    }
                }
            }

            Debug.Log($"Processed {processedCount} materials out of {totalMaterials}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error during shader replacement: {e.Message}");
        }
    }
}
