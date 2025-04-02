using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class MaterialPathExporter : EditorWindow
{
    private List<MaterialInfo> materials = new List<MaterialInfo>();
    private string filePath = Path.Combine(Application.dataPath, "../MaterialPaths.json");

    [System.Serializable]
    private struct MaterialInfo
    {
        public string materialPath;
        public string shaderPath;
    }

    [MenuItem("Tools/Material Path Exporter")]
    static void OpenWindow()
    {
        GetWindow<MaterialPathExporter>("Material Path Exporter");
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);

        if (GUILayout.Button("Export Material Paths"))
        {
            ExportMaterialPaths();
        }

        if (GUILayout.Button("Import Material Paths"))
        {
            ImportMaterialPaths();
        }

        filePath = EditorGUILayout.TextField("File Path:", filePath);

        EditorGUILayout.LabelField($"Total Materials Found: {materials.Count}");
    }

    private void ExportMaterialPaths()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material");
        materials.Clear();

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            Material material = AssetDatabase.LoadAssetPath<Material>(assetPath);

            if (material != null)
            {
                materials.Add(
                    new MaterialInfo { materialPath = assetPath, shaderPath = material.shader.name }
                );
            }
        }

        string directory = Path.GetDirectoryName(filePath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonUtility.ToJson(
            new MaterialListWrapper { materials = materials.ToArray() }
        );
        File.WriteAllText(filePath, json);

        Debug.Log($"Successfully exported {materials.Count} materials to {filePath}");
        AssetDatabase.Refresh();
    }

    private void ImportMaterialPaths()
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError("File not found: " + filePath);
            return;
        }

        string json = File.ReadAllText(filePath);
        MaterialListWrapper wrapper = JsonUtility.FromJson<MaterialListWrapper>(json);

        int updatedMaterials = 0;
        int skippedMaterials = 0;

        foreach (MaterialInfo info in wrapper.materials)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(info.materialPath);
            if (material != null)
            {
                if (material.shader != null && material.shader.name == info.shaderPath)
                {
                    skippedMaterials++;
                    Debug.Log($"Skipping {info.materialPath}: Shader matches");
                    continue;
                }

                Shader shader = Shader.Find(info.shaderPath);
                if (shader != null)
                {
                    material.shader = shader;
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssets();
                    updatedMaterials++;
                    Debug.Log($"Updated {info.materialPath} to shader: {info.shaderPath}");
                }
                else
                {
                    Debug.LogWarning(
                        $"Could not find shader: {info.shaderPath} for material: {info.materialPath}"
                    );
                }
            }
            else
            {
                Debug.LogWarning($"Could not find material: {info.materialPath}");
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"Import completed:");
        Debug.Log($"- Updated materials: {updatedMaterials}");
        Debug.Log($"- Skipped materials: {skippedMaterials}");
        Debug.Log($"- Total processed: {wrapper.materials.Length}");
    }

    [System.Serializable]
    private class MaterialListWrapper
    {
        public MaterialInfo[] materials;
    }
}
