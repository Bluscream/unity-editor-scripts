using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class RemoveMissing
{
    [MenuItem("Auto/Remove Missing Scripts Recursively")]
    private static void FindAndRemoveMissingInSelected()
    {
        try
        {
            UnityEngine.Object[] deepSelection = null;
            
            // EditorUtility.CollectDeepHierarchy might be deprecated in newer versions
            #if UNITY_2020_1_OR_NEWER
            // Use alternative method for newer Unity versions
            var allObjects = new List<UnityEngine.Object>();
            foreach (var go in Selection.gameObjects)
            {
                if (go != null)
                {
                    allObjects.Add(go);
                    CollectChildren(go.transform, allObjects);
                }
            }
            deepSelection = allObjects.ToArray();
            #else
            deepSelection = EditorUtility.CollectDeepHierarchy(Selection.gameObjects);
            #endif
            
            int compCount = 0;
            int goCount = 0;
            foreach (var o in deepSelection)
            {
                if (o is GameObject go)
                {
                    int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                    if (count > 0)
                    {
                        Undo.RegisterCompleteObjectUndo(go, "Remove missing scripts");
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                        compCount += count;
                        goCount++;
                    }
                }
            }
            Debug.Log($"Found and removed {compCount} missing scripts from {goCount} GameObjects");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error removing missing scripts: {e.Message}\n{e.StackTrace}");
        }
    }
    
    private static void CollectChildren(Transform parent, List<UnityEngine.Object> collection)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child != null)
            {
                collection.Add(child.gameObject);
                CollectChildren(child, collection);
            }
        }
    }
}
