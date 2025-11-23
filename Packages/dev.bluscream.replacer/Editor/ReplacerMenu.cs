using UnityEditor;
using UnityEngine;

namespace Bluscream.Replacer
{
    /// <summary>
    /// Context menu items for GameObject replacement
    /// </summary>
    public static class ReplacerMenu
    {
        [MenuItem("GameObject/Replace with ...", false, 10)]
        public static void SetSourceFromGameObject()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("No Selection", "Please select a GameObject in the hierarchy.", "OK");
                return;
            }

            GameObjectReplacer.SetSourceGameObject(selected);
            EditorUtility.DisplayDialog("Source Set", $"Source GameObject set to:\n{selected.name}\n\nRight-click on another GameObject and select '... Replace this' to replace it.", "OK");
        }

        [MenuItem("GameObject/Replace with ...", true)]
        public static bool ValidateSetSourceFromGameObject()
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem("Assets/Replace with ...", false, 1)]
        public static void SetSourceFromPrefab()
        {
            UnityEngine.Object selected = Selection.activeObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("No Selection", "Please select a prefab in the Project window.", "OK");
                return;
            }

            GameObject prefab = selected as GameObject;
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Invalid Selection", "Please select a prefab GameObject.", "OK");
                return;
            }

            // Check if it's actually a prefab
            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog("Invalid Selection", "Selected object is not a prefab asset.", "OK");
                return;
            }

            GameObjectReplacer.SetSourcePrefab(prefab);
            EditorUtility.DisplayDialog("Source Set", $"Source Prefab set to:\n{prefab.name}\n\nRight-click on a GameObject in the hierarchy and select '... Replace this' to replace it.", "OK");
        }

        [MenuItem("Assets/Replace with ...", true)]
        public static bool ValidateSetSourceFromPrefab()
        {
            UnityEngine.Object selected = Selection.activeObject;
            if (selected == null) return false;

            GameObject prefab = selected as GameObject;
            if (prefab == null) return false;

            string path = AssetDatabase.GetAssetPath(prefab);
            return !string.IsNullOrEmpty(path);
        }

        [MenuItem("GameObject/... Replace this", false, 11)]
        public static void ReplaceThisGameObject()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("No Selection", "Please select a GameObject in the hierarchy to replace.", "OK");
                return;
            }

            if (!GameObjectReplacer.HasSource())
            {
                EditorUtility.DisplayDialog("No Source", "No source GameObject or prefab set.\n\nRight-click on a GameObject or prefab and select 'Replace with ...' first.", "OK");
                return;
            }

            string sourceName = GameObjectReplacer.GetSourceName();
            if (EditorUtility.DisplayDialog(
                "Replace GameObject",
                $"Replace '{selected.name}' with '{sourceName}'?\n\nThe target will be renamed and disabled, and the source will be placed in its position.",
                "Replace",
                "Cancel"))
            {
                GameObjectReplacer.ReplaceGameObject(selected);
            }
        }

        [MenuItem("GameObject/... Replace this", true)]
        public static bool ValidateReplaceThisGameObject()
        {
            return Selection.activeGameObject != null && GameObjectReplacer.HasSource();
        }
    }
}
