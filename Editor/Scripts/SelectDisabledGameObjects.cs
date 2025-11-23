using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SelectDisabledGameObjects : EditorWindow
{
    [MenuItem("Tools/Select all disabled GameObjects")]
    private static void SelectAllDisabledGameObjects()
    {
        try
        {
            var toSelect = new List<GameObject>();
            List<GameObject> rootObjects = new List<GameObject>();
            Scene scene = SceneManager.GetActiveScene();
            
            // Unity 2019.3+ uses GetRootGameObjects with List parameter
            // Older versions use GetRootGameObjects() which returns GameObject[]
            #if UNITY_2019_3_OR_NEWER
            scene.GetRootGameObjects(rootObjects);
            #else
            GameObject[] rootArray = scene.GetRootGameObjects();
            if (rootArray != null)
            {
                rootObjects.AddRange(rootArray);
            }
            #endif
            
            for (int i = 0; i < rootObjects.Count; ++i)
            {
                GameObject gameObject = rootObjects[i];
                if (gameObject != null && !gameObject.activeSelf)
                {
                    toSelect.Add(gameObject);
                }
                SelectDisabledChildren(gameObject, toSelect);
            }

            Selection.objects = toSelect.ToArray();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error selecting disabled GameObjects: {e.Message}\n{e.StackTrace}");
        }
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
