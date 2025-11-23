using UnityEditor;
using UnityEngine;
using System.IO;
using Bluscream.Cleanup;

namespace Bluscream.Cleanup
{
    /// <summary>
    /// Context menu items for asset cleanup
    /// </summary>
    public static class CleanupMenu
    {
        [MenuItem("Assets/Cleanup/Cleanup This Folder (Recursive)", false, 2000)]
        public static void CleanupFolderRecursive()
        {
            string folderPath = GetSelectedFolderPath();
            if (string.IsNullOrEmpty(folderPath))
            {
                EditorUtility.DisplayDialog("No Folder Selected", "Please select a folder in the Project window.", "OK");
                return;
            }

            CleanupWindow window = CleanupWindow.ShowWindow();
            if (window != null)
            {
                window.targetFolder = folderPath;
                window.recursive = true;
            }
        }

        [MenuItem("Assets/Cleanup/Cleanup This Folder (Non-Recursive)", false, 2001)]
        public static void CleanupFolderNonRecursive()
        {
            string folderPath = GetSelectedFolderPath();
            if (string.IsNullOrEmpty(folderPath))
            {
                EditorUtility.DisplayDialog("No Folder Selected", "Please select a folder in the Project window.", "OK");
                return;
            }

            CleanupWindow window = CleanupWindow.ShowWindow();
            if (window != null)
            {
                window.targetFolder = folderPath;
                window.recursive = false;
            }
        }

        [MenuItem("Assets/Cleanup/Cleanup This Folder (Recursive)", true)]
        [MenuItem("Assets/Cleanup/Cleanup This Folder (Non-Recursive)", true)]
        public static bool ValidateCleanupFolder()
        {
            string folderPath = GetSelectedFolderPath();
            return !string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath);
        }

        private static string GetSelectedFolderPath()
        {
            foreach (Object obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                {
                    return path;
                }
            }
            return null;
        }
    }
}
