using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EPPZ.Editor
{
    public class SelectByLayer : EditorWindow
    {
        static int layerIndex;

        [MenuItem("Window/eppz!/Select by Layer")]
        public static void Init()
        {
            SelectByLayer window = EditorWindow.GetWindow<SelectByLayer>("Select by Layer");
            window.Show();
            window.Focus();
        }

        void OnGUI()
        {
            layerIndex = EditorGUILayout.IntField("Layer index", layerIndex);

            if (GUILayout.Button("Select all GameObjects (and Prefabs) on Layer"))
            {
                FindAndSelectObjectsByLayer();
            }
        }

        public static void FindAndSelectObjectsByLayer()
        {
            try
            {
                GameObject[] objects;
                
                // Resources.FindObjectsOfTypeAll is deprecated, use Object.FindObjectsOfType instead
                // But we need all objects including inactive ones, so we use the deprecated method
                // with a fallback for newer Unity versions
                #if UNITY_2023_1_OR_NEWER
                // Unity 2023.1+ has FindObjectsByType with FindObjectsInactive.Include
                objects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(gameObject => gameObject.hideFlags == HideFlags.None)
                    .ToArray();
                #elif UNITY_2020_1_OR_NEWER
                // Unity 2020.1+ has FindObjectsOfType with includeInactive parameter
                objects = Object.FindObjectsOfType<GameObject>(true)
                    .Where(gameObject => gameObject.hideFlags == HideFlags.None)
                    .ToArray();
                #else
                // Fallback to deprecated method for older versions
                objects = Resources
                    .FindObjectsOfTypeAll<GameObject>()
                    .Where(gameObject => gameObject.hideFlags == HideFlags.None)
                    .ToArray();
                #endif
                
                List<GameObject> matches = new List<GameObject>();
                foreach (GameObject eachGameObject in objects)
                {
                    if (eachGameObject != null && eachGameObject.layer == layerIndex)
                    {
                        matches.Add(eachGameObject);
                    }
                }
                Selection.objects = matches.ToArray();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error finding objects by layer: {e.Message}\n{e.StackTrace}");
            }
        }
    }
}
