using System;
using System.Collections.Generic;

namespace Bluscream.BackupSystem
{
    /// <summary>
    /// Backup scope options
    /// </summary>
    public enum BackupScope
    {
        AllAssets,
        SingleGameObject,
        GameObjectRecursive
    }

    /// <summary>
    /// Complete backup data structure
    /// </summary>
    [System.Serializable]
    public class BackupData
    {
        public string version = "1.0.0";
        public string timestamp;
        public string backupName;
        public BackupScope scope;
        public string targetPath; // GameObject path or "ALL_ASSETS"
        public List<MaterialBackup> materials = new List<MaterialBackup>();
        public List<ComponentBackup> components = new List<ComponentBackup>();
        public List<TextureBackup> textures = new List<TextureBackup>();
        public List<GameObjectBackup> gameObjects = new List<GameObjectBackup>();
    }
}
