using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class MissingScriptUtility : EditorWindow
{
    bool includeInactive = true;
    bool includePrefabs = true;

    [MenuItem("Window/MissingScriptUtility")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow(typeof(MissingScriptUtility));
    }

    public void OnGUI()
    {
        string includeInactiveTooltip = "Whether to include inactive GameObjects in the search.";
        includeInactive = EditorGUILayout.Toggle(
            new GUIContent("Include Inactive", includeInactiveTooltip),
            includeInactive
        );

        string includePrefabsTooltip = "Whether to include prefab GameObjects in the search.";
        includePrefabs = EditorGUILayout.Toggle(
            new GUIContent("Include Prefabs", includePrefabsTooltip),
            includePrefabs
        );

        if (GUILayout.Button("Log Missing Scripts"))
        {
            try
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                GameObject[] rootObjects;
                #if UNITY_2019_3_OR_NEWER
                var rootList = new List<GameObject>();
                scene.GetRootGameObjects(rootList);
                rootObjects = rootList.ToArray();
                #else
                rootObjects = scene.GetRootGameObjects();
                #endif
                LogMissingScripts(rootObjects);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error logging missing scripts: {e.Message}\n{e.StackTrace}");
            }
        }
        if (GUILayout.Button("Log Missing Scripts from Selected GameObjects"))
        {
            try
            {
                LogMissingScripts(SelectedGameObjects(includeInactive, includePrefabs));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error logging missing scripts from selection: {e.Message}\n{e.StackTrace}");
            }
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Select GameObjects with Missing Scripts"))
        {
            try
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                GameObject[] rootObjects;
                #if UNITY_2019_3_OR_NEWER
                var rootList = new List<GameObject>();
                scene.GetRootGameObjects(rootList);
                rootObjects = rootList.ToArray();
                #else
                rootObjects = scene.GetRootGameObjects();
                #endif
                SelectGameObjectsWithMissingScripts(rootObjects);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error selecting GameObjects with missing scripts: {e.Message}\n{e.StackTrace}");
            }
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Remove Missing Scripts"))
        {
            try
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                GameObject[] rootObjects;
                #if UNITY_2019_3_OR_NEWER
                var rootList = new List<GameObject>();
                scene.GetRootGameObjects(rootList);
                rootObjects = rootList.ToArray();
                #else
                rootObjects = scene.GetRootGameObjects();
                #endif
                RemoveMissingScripts(rootObjects);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error removing missing scripts: {e.Message}\n{e.StackTrace}");
            }
        }
        if (GUILayout.Button("Remove Missing Scripts from Selected GameObjects"))
        {
            try
            {
                RemoveMissingScripts(SelectedGameObjects(includeInactive, includePrefabs));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error removing missing scripts from selection: {e.Message}\n{e.StackTrace}");
            }
        }
    }

    public static void LogMissingScripts(GameObject[] gameObjects)
    {
        try
        {
            if (gameObjects == null)
            {
                Debug.LogWarning("GameObjects array is null");
                return;
            }

            int gameObjectCount = 0;
            int missingScriptCount = 0;
            foreach (GameObject gameObject in gameObjects)
            {
                if (gameObject != null)
                {
                    missingScriptCount += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        gameObject
                    );
                    ++gameObjectCount;
                }
            }

            Debug.Log(
                string.Format(
                    "Searched {0} GameObjects and found {1} missing scripts.",
                    gameObjectCount,
                    missingScriptCount
                )
            );
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error logging missing scripts: {e.Message}\n{e.StackTrace}");
        }
    }

    public static void SelectGameObjectsWithMissingScripts(GameObject[] gameObjects)
    {
        try
        {
            if (gameObjects == null)
            {
                Debug.LogWarning("GameObjects array is null");
                return;
            }

            List<GameObject> selections = new List<GameObject>();

            foreach (GameObject gameObject in gameObjects)
            {
                if (gameObject != null && GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject) > 0)
                    selections.Add(gameObject);
            }

            Selection.objects = selections.ToArray();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error selecting GameObjects with missing scripts: {e.Message}\n{e.StackTrace}");
        }
    }

    public static void RemoveMissingScripts(GameObject[] gameObjects)
    {
        try
        {
            if (gameObjects == null)
            {
                Debug.LogWarning("GameObjects array is null");
                return;
            }

            int missingScriptCount = 0;
            foreach (GameObject gameObject in gameObjects)
            {
                if (gameObject != null)
                {
                    int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                    if (count > 0)
                    {
                        Undo.RegisterCompleteObjectUndo(gameObject, "Remove missing scripts");
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
                        missingScriptCount += count;
                    }
                }
            }

            Debug.Log(
                string.Format(
                    "Searched {0} GameObjects and removed {1} missing scripts.",
                    gameObjects.Length,
                    missingScriptCount
                )
            );
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error removing missing scripts: {e.Message}\n{e.StackTrace}");
        }
    }

    #region Sub-utilities

    public static GameObject[] SelectedGameObjects(
        bool includingInactive = true,
        bool includingPrefabs = true
    )
    {
        List<GameObject> selectedGameObjects = new List<GameObject>(Selection.gameObjects);
        foreach (GameObject selectedGameObject in Selection.gameObjects)
        {
            Transform[] childTransforms = selectedGameObject.GetComponentsInChildren<Transform>(
                includingInactive
            );
            foreach (Transform childTransform in childTransforms)
                selectedGameObjects.Add(childTransform.gameObject);
            if (includingPrefabs)
            {
                HashSet<GameObject> prefabs = new HashSet<GameObject>();
                PrefabInstances(selectedGameObject, prefabs);
                selectedGameObjects.AddRange(prefabs);
            }
        }

        return selectedGameObjects.ToArray();
    }

    public static int RecursiveMissingScriptCount(GameObject gameObject)
    {
        int missingScriptCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
            gameObject
        );

        Transform[] childTransforms = gameObject.GetComponentsInChildren<Transform>(true);
        foreach (Transform childTransform in childTransforms)
            missingScriptCount += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                childTransform.gameObject
            );

        return missingScriptCount;
    }

    private static void PrefabInstances(GameObject instance, HashSet<GameObject> prefabs)
    {
        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
        if (source == null || !prefabs.Add(source))
            return;

        PrefabInstances(source, prefabs);
    }

    #endregion
}
