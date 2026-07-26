using System;
using UnityEngine;

namespace Bluscream.BackupSystem
{
    /// <summary>
    /// Comprehensive metadata about a backup
    /// </summary>
    [System.Serializable]
    public class BackupMetadata
    {
        // Backup System Information
        public string version = "1.0.0";
        public string timestamp;
        public string backupName;
        public string backupLocation; // Full path where backup is stored
        public BackupScope scope;
        public string targetPath; // GameObject path or "ALL_ASSETS"

        // Unity Environment Information
        public string unityVersion;
        public string projectName;
        public string projectPath;
        public string platform; // Current build target platform

        // What Was Backed Up (relative paths to backup files, empty string if not backed up)
        public string backedUpMaterials; // e.g., "materials.json" or ""
        public string backedUpComponents; // e.g., "components.json" or ""
        public string backedUpTextures; // e.g., "textures.json" or ""
        public string backedUpGameObjectHierarchy; // e.g., "hierarchy.json" or ""
        public bool includedMaterialProperties;
        public bool includedComponentData;

        // Additional Information
        public string createdBy = "Backup System";
        public string notes = "";

        /// <summary>
        /// Creates metadata from backup configuration and results
        /// </summary>
        public static BackupMetadata Create(BackupConfig config, BackupScope scope, string backupFolder, string targetPath)
        {
            return new BackupMetadata
            {
                version = "1.0.0",
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), // ISO 8601 format
                backupName = config.backupName,
                backupLocation = backupFolder,
                scope = scope,
                targetPath = targetPath,
                unityVersion = Application.unityVersion,
                projectName = Application.productName,
                projectPath = Application.dataPath.Replace("/Assets", ""),
                platform = Application.platform.ToString(),
                backedUpMaterials = config.backupMaterials ? "materials.json" : "",
                backedUpComponents = config.backupComponents ? "components.json" : "",
                backedUpTextures = config.backupTextures ? "textures.json" : "",
                backedUpGameObjectHierarchy = config.backupGameObjectHierarchy ? "hierarchy.json" : "",
                includedMaterialProperties = config.includeMaterialProperties,
                includedComponentData = config.includeComponentData,
                createdBy = "Backup System",
                notes = ""
            };
        }
    }
}
