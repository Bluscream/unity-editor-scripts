using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SelectDisabledGameObjects : EditorWindow
{
    [MenuItem("Tools/Select all disabled GameObjects")]
    private static void SelectAllDisabledGameObjects()
    {
        var toSelect = new List<GameObject>();
        List<GameObject> rootObjects = new List<GameObject>();
        Scene scene = SceneManager.GetActiveScene();
        scene.GetRootGameObjects(rootObjects);
        for (int i = 0; i < rootObjects.Count; ++i)
        {
            GameObject gameObject = rootObjects[i];
            if (!gameObject.activeSelf)
            {
                toSelect.Add(gameObject);
            }
            SelectDisabledChildren(gameObject, toSelect);
        }

        Selection.objects = toSelect.ToArray();
    }

    private static void SelectDisabledChildren(GameObject parent, List<GameObject> toSelect)
    {
        int childCount = parent.transform.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform childTransform = parent.transform.GetChild(i);
            GameObject childObject = childTransform.gameObject;

            if (!childObject.activeSelf)
            {
                toSelect.Add(childObject);
                continue;
            }
            SelectDisabledChildren(childObject, toSelect);
        }
    }
}
