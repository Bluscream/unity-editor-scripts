using UnityEngine;
using UnityEditor;

public class CopyGameObjectPath
{
    [MenuItem("GameObject/Copy Path/Slash Format", false, 0)]
    static void CopyPathSlash()
    {
        if (Selection.activeGameObject != null)
        {
            string path = GetGameObjectPath(Selection.activeGameObject, "/");
            GUIUtility.systemCopyBuffer = path;
            Debug.Log($"Copied path (slash format): {path}");
        }
    }

    [MenuItem("GameObject/Copy Path/Underscore Format", false, 1)]
    static void CopyPathUnderscore()
    {
        if (Selection.activeGameObject != null)
        {
            string path = GetGameObjectPath(Selection.activeGameObject, "_");
            GUIUtility.systemCopyBuffer = path;
            Debug.Log($"Copied path (underscore format): {path}");
        }
    }

    [MenuItem("GameObject/Copy Path/Slash Format", true)]
    [MenuItem("GameObject/Copy Path/Underscore Format", true)]
    static bool ValidateCopyPath()
    {
        return Selection.activeGameObject != null;
    }

    static string GetGameObjectPath(GameObject obj, string separator)
    {
        string path = obj.name;
        Transform parent = obj.transform.parent;

        while (parent != null)
        {
            path = parent.name + separator + path;
            parent = parent.parent;
        }

        return path;
    }
}
