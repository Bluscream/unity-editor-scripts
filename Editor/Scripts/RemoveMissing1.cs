using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class RemoveMissing
{
    [MenuItem("Auto/Remove Missing Scripts Recursively")]
    private static void FindAndRemoveMissingInSelected()
    {
        var deepSelection = EditorUtility.CollectDeepHierarchy(Selection.gameObjects);
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
}
