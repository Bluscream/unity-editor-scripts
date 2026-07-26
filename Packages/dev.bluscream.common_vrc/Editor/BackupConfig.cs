using System;

namespace Bluscream.BackupSystem
{
    /// <summary>
    /// Configuration for backup operations
    /// </summary>
    [System.Serializable]
    public class BackupConfig
    {
        public bool backupMaterials = true;
        public bool backupComponents = true;
        public bool backupTextures = true;
        public bool backupGameObjectHierarchy = true;
        public bool includeMaterialProperties = true;
        public bool includeComponentData = true;
        public string backupLocation = "Assets/Backups";
        public string backupName = "";
    }
}
