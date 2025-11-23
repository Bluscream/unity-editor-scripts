using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class FindMissingScriptsRecursively
{
    [MenuItem("Auto/Remove Missing Scripts Recursively Visit Prefabs")]
    private static void FindAndRemoveMissingInSelected()
    {
        var deeperSelection = Selection
            .gameObjects.SelectMany(go => go.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject);
        var prefabs = new HashSet<Object>();
        int compCount = 0;
        int goCount = 0;
        foreach (var go in deeperSelection)
        {
            int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (count > 0)
            {
                bool isPartOfPrefab = false;
                #if UNITY_2018_3_OR_NEWER
                // Unity 2018.3+ uses IsPartOfAnyPrefab
                isPartOfPrefab = PrefabUtility.IsPartOfAnyPrefab(go);
                #else
                // Older versions use GetPrefabType
                isPartOfPrefab = PrefabUtility.GetPrefabType(go) != PrefabType.None;
                #endif
                if (isPartOfPrefab)
                {
                    RecursivePrefabSource(go, prefabs, ref compCount, ref goCount);
                    count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                    if (count == 0)
                        continue;
                }

                Undo.RegisterCompleteObjectUndo(go, "Remove missing scripts");
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                compCount += count;
                goCount++;
            }
        }

        Debug.Log($"Found and removed {compCount} missing scripts from {goCount} GameObjects");
    }

    private static void RecursivePrefabSource(
        GameObject instance,
        HashSet<Object> prefabs,
        ref int compCount,
        ref int goCount
    )
    {
        var source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
        if (source == null || !prefabs.Add(source))
            return;
        RecursivePrefabSource(source, prefabs, ref compCount, ref goCount);

        int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(source);
        if (count > 0)
        {
            Undo.RegisterCompleteObjectUndo(source, "Remove missing scripts");
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(source);
            compCount += count;
            goCount++;
        }
    }
}
