using System.Collections.Generic;
using UnityEngine;

namespace Bluscream.BackupSystem
{
    /// <summary>
    /// GameObject backup data structure
    /// </summary>
    [System.Serializable]
    public class GameObjectBackup
    {
        public string gameObjectPath;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public bool activeSelf;
        
        /// <summary>
        /// Gets the parent path from the gameObjectPath by removing the last segment
        /// </summary>
        public string GetParentPath()
        {
            if (string.IsNullOrEmpty(gameObjectPath))
                return "";
            
            int lastSlash = gameObjectPath.LastIndexOf('/');
            if (lastSlash < 0)
                return ""; // Root object, no parent
            
            return gameObjectPath.Substring(0, lastSlash);
        }
    }

    /// <summary>
    /// Handles GameObject hierarchy backup operations
    /// </summary>
    public static class GameObjectBackupHandler
    {
        /// <summary>
        /// Backs up GameObject hierarchy
        /// </summary>
        public static List<GameObjectBackup> BackupGameObjectHierarchy(GameObject root, bool recursive)
        {
            List<GameObjectBackup> backups = new List<GameObjectBackup>();

            if (root == null)
                return backups;

            if (recursive)
            {
                Transform[] allTransforms = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in allTransforms)
                {
                    backups.Add(CreateGameObjectBackup(t.gameObject));
                }
            }
            else
            {
                backups.Add(CreateGameObjectBackup(root));
            }

            return backups;
        }

        /// <summary>
        /// Creates a GameObject backup entry
        /// </summary>
        private static GameObjectBackup CreateGameObjectBackup(GameObject go)
        {
            return new GameObjectBackup
            {
                gameObjectPath = Utils.GetGameObjectPath(go),
                localPosition = go.transform.localPosition,
                localRotation = go.transform.localRotation,
                localScale = go.transform.localScale,
                activeSelf = go.activeSelf
            };
        }

        /// <summary>
        /// Restores GameObjects from backup
        /// </summary>
        public static void RestoreGameObjects(List<GameObjectBackup> gameObjects, List<ComponentBackup> components, bool includeComponentData)
        {
            // GameObject restoration is complex and may not always be possible
            // This would require scene management and hierarchy reconstruction
            Debug.LogWarning("GameObject restoration is not fully implemented. Manual restoration may be required.");
        }
    }
}
