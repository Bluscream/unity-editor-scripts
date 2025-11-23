using UnityEditor;
using UnityEngine;

namespace FLGCoreEditor.Utilities
{
    public class FindMissingScriptsRecursivelyAndRemove : EditorWindow
    {
        private static int _goCount;
        private static int _componentsCount;
        private static int _missingCount;

        private static bool _bHaveRun;

        [MenuItem("FLGCore/Editor/Utility/FindMissingScriptsRecursivelyAndRemove")]
        public static void ShowWindow()
        {
            GetWindow(typeof(FindMissingScriptsRecursivelyAndRemove));
        }

        public void OnGUI()
        {
            if (GUILayout.Button("Find Missing Scripts in selected GameObjects"))
            {
                FindInSelected();
            }

            if (!_bHaveRun)
                return;

            EditorGUILayout.TextField($"{_goCount} GameObjects Selected");
            if (_goCount > 0)
                EditorGUILayout.TextField($"{_componentsCount} Components");
            if (_goCount > 0)
                EditorGUILayout.TextField($"{_missingCount} Deleted");
        }

        private static void FindInSelected()
        {
            try
            {
                var go = Selection.gameObjects;
                if (go == null || go.Length == 0)
                {
                    Debug.LogWarning("No GameObjects selected");
                    return;
                }

                _goCount = 0;
                _componentsCount = 0;
                _missingCount = 0;
                foreach (var g in go)
                {
                    if (g != null)
                    {
                        FindInGo(g);
                    }
                }

                _bHaveRun = true;
                Debug.Log(
                    $"Searched {_goCount} GameObjects, {_componentsCount} components, found {_missingCount} missing"
                );

                AssetDatabase.SaveAssets();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error finding missing scripts: {e.Message}\n{e.StackTrace}");
            }
        }

        private static void FindInGo(GameObject g)
        {
            try
            {
                if (g == null)
                    return;

                _goCount++;
                var components = g.GetComponents<Component>();

                if (components == null)
                    return;

                var r = 0;

                for (var i = 0; i < components.Length; i++)
                {
                    _componentsCount++;
                    if (components[i] != null)
                        continue;
                    _missingCount++;
                    var s = g.name;
                    var t = g.transform;
                    while (t != null && t.parent != null)
                    {
                        s = t.parent.name + "/" + s;
                        t = t.parent;
                    }

                    Debug.Log($"{s} has a missing script at {i}", g);

                    try
                    {
                        var serializedObject = new SerializedObject(g);

                        var prop = serializedObject.FindProperty("m_Component");

                        if (prop != null)
                        {
                            prop.DeleteArrayElementAtIndex(i - r);
                            r++;

                            serializedObject.ApplyModifiedProperties();
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"Error removing missing script at index {i} from {g.name}: {e.Message}");
                    }
                }

                if (g.transform != null)
                {
                    foreach (Transform childT in g.transform)
                    {
                        if (childT != null && childT.gameObject != null)
                        {
                            FindInGo(childT.gameObject);
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Error processing GameObject {g?.name}: {e.Message}");
            }
        }
    }
}
