using System.Collections.Generic;
using System.IO;
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
        try
        {
            string[] guids = AssetDatabase.FindAssets("t:Material");
            materials.Clear();

            foreach (string guid in guids)
            {
                try
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(assetPath))
                        continue;

                    Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);

                    if (material != null && material.shader != null)
                    {
                        materials.Add(
                            new MaterialInfo { materialPath = assetPath, shaderPath = material.shader.name }
                        );
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Failed to process material with GUID {guid}: {e.Message}");
                }
            }

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
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
        catch (System.Exception e)
        {
            Debug.LogError($"Error exporting material paths: {e.Message}\n{e.StackTrace}");
        }
    }

    private void ImportMaterialPaths()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError("File not found: " + filePath);
                return;
            }

            string json = File.ReadAllText(filePath);
            MaterialListWrapper wrapper = JsonUtility.FromJson<MaterialListWrapper>(json);

            if (wrapper == null || wrapper.materials == null)
            {
                Debug.LogError("Failed to parse JSON file or materials array is null");
                return;
            }

            int updatedMaterials = 0;
            int skippedMaterials = 0;
            int errorMaterials = 0;

            foreach (MaterialInfo info in wrapper.materials)
            {
                try
                {
                    if (string.IsNullOrEmpty(info.materialPath))
                        continue;

                    Material material = AssetDatabase.LoadAssetAtPath<Material>(info.materialPath);
                    if (material != null)
                    {
                        if (material.shader != null && material.shader.name == info.shaderPath)
                        {
                            skippedMaterials++;
                            continue;
                        }

                        Shader shader = Shader.Find(info.shaderPath);
                        if (shader != null)
                        {
                            material.shader = shader;
                            EditorUtility.SetDirty(material);
                            updatedMaterials++;
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"Could not find shader: {info.shaderPath} for material: {info.materialPath}"
                            );
                            errorMaterials++;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Could not find material: {info.materialPath}");
                        errorMaterials++;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Error processing material {info.materialPath}: {e.Message}");
                    errorMaterials++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Import completed:");
            Debug.Log($"- Updated materials: {updatedMaterials}");
            Debug.Log($"- Skipped materials: {skippedMaterials}");
            Debug.Log($"- Errors: {errorMaterials}");
            Debug.Log($"- Total processed: {wrapper.materials.Length}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error importing material paths: {e.Message}\n{e.StackTrace}");
        }
    }

    [System.Serializable]
    private class MaterialListWrapper
    {
        public MaterialInfo[] materials;
    }
}
