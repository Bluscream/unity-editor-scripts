using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class FindMissingScriptsWindow : EditorWindow
{
    [MenuItem("Window/Missing Script Window")]
    private static void Init()
    {
        GetWindow<FindMissingScriptsWindow>("Missing Script Finder").Show();
    }

    public List<GameObject> results = new List<GameObject>();

    private void OnGUI()
    {
        if (GUILayout.Button("Search Project"))
            SearchProject();
        if (GUILayout.Button("Search scene"))
            SearchScene();
        if (GUILayout.Button("Search Selected Objects"))
            SearchSelected();
        if (GUILayout.Button("Remove Selected Objects"))
            RemoveScripts();
        var so = new SerializedObject(this);
        var resultsProperty = so.FindProperty(nameof(results));
        EditorGUILayout.PropertyField(resultsProperty, true);
        so.ApplyModifiedProperties();
    }

    private void SearchProject()
    {
        try
        {
            results = AssetDatabase
                .FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(x => x != null && IsMissing(x, true))
                .Distinct()
                .ToList();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error searching project: {e.Message}\n{e.StackTrace}");
            results = new List<GameObject>();
        }
    }

    private void SearchScene()
    {
        try
        {
            // FindObjectsOfType is deprecated, use Object.FindObjectsOfType instead
            #if UNITY_2023_1_OR_NEWER
            results = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(x => x != null && IsMissing(x, false))
                .Distinct()
                .ToList();
            #else
            results = Object.FindObjectsOfType<GameObject>(true)
                .Where(x => x != null && IsMissing(x, false))
                .Distinct()
                .ToList();
            #endif
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error searching scene: {e.Message}\n{e.StackTrace}");
            results = new List<GameObject>();
        }
    }

    private void SearchSelected()
    {
        try
        {
            results = Selection.gameObjects
                .Where(x => x != null && IsMissing(x, false))
                .Distinct()
                .ToList();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error searching selected objects: {e.Message}\n{e.StackTrace}");
            results = new List<GameObject>();
        }
    }

    private void RemoveScripts()
    {
        try
        {
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] != null)
                {
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(results[i]);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error removing scripts: {e.Message}\n{e.StackTrace}");
        }
    }

    private static bool IsMissing(GameObject go, bool includeChildren)
    {
        try
        {
            if (go == null)
                return false;

            var components = includeChildren
                ? go.GetComponentsInChildren<Component>()
                : go.GetComponents<Component>();

            return components != null && components.Any(x => x == null);
        }
        catch (System.Exception)
        {
            return false;
        }
    }
}
