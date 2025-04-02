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
            GameObject[] objects = Resources
                .FindObjectsOfTypeAll<GameObject>()
                .Where(gameObject => gameObject.hideFlags == HideFlags.None)
                .ToArray();
            List<GameObject> matches = new List<GameObject>();
            foreach (GameObject eachGameObject in objects)
            {
                if (eachGameObject.layer == layerIndex)
                {
                    matches.Add(eachGameObject);
                }
            }
            Selection.objects = matches.ToArray();
        }
    }
}
