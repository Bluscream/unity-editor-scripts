using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Applies the humanoid rig hygiene steps from the tutorial series' Unity rig pass: unmapping the jaw
    /// bone and enabling Legacy Blend Shape Normals on the source model.
    ///
    /// Neither is required for a successful upload, so both are opt-in. Both also edit the shared model
    /// importer, which affects every avatar using that FBX — not just the clone being optimized — so this
    /// pass is deliberately kept out of the default pipeline.
    /// </summary>
    public static class AvatarRigOptimizer
    {
        /// <summary>
        /// Unmaps the humanoid jaw bone. VRChat drives the jaw from visemes, and a mapped jaw bone fights
        /// that, which shows up as a mouth that will not move or moves wrongly while talking.
        /// </summary>
        /// <returns>True if a mapping was changed.</returns>
        public static bool UnmapJawBone(GameObject avatarRoot, Action<string> progressCallback = null)
        {
            if (avatarRoot == null) return false;

            Animator animator = avatarRoot.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
            {
                Debug.Log("[AvatarRigOptimizer] Avatar is not humanoid — no jaw bone to unmap.");
                return false;
            }

            Transform jaw = animator.GetBoneTransform(HumanBodyBones.Jaw);
            if (jaw == null)
            {
                Debug.Log("[AvatarRigOptimizer] Humanoid rig has no jaw bone mapped — nothing to do.");
                return false;
            }

            ModelImporter importer = GetModelImporter(animator.avatar);
            if (importer == null)
            {
                Debug.LogWarning($"[AvatarRigOptimizer] Jaw bone '{jaw.name}' is mapped, but its source model importer could not be found — unmap it manually via the rig's Configure menu.");
                return false;
            }

            progressCallback?.Invoke("Unmapping humanoid jaw bone...");

            HumanDescription description = importer.humanDescription;
            HumanBone[] humanBones = description.human;
            string jawName = HumanTrait.BoneName[(int)HumanBodyBones.Jaw];

            int index = Array.FindIndex(humanBones, b => b.humanName == jawName);
            if (index < 0)
            {
                Debug.Log("[AvatarRigOptimizer] Jaw is not present in the model's human description — nothing to unmap.");
                return false;
            }

            var remaining = humanBones.Where((_, i) => i != index).ToArray();
            description.human = remaining;
            importer.humanDescription = description;

            AssetDatabase.WriteImportSettingsIfDirty(importer.assetPath);
            importer.SaveAndReimport();

            Debug.Log($"[AvatarRigOptimizer] Unmapped jaw bone '{jaw.name}' on model '{importer.assetPath}'. Note: this affects every avatar using that model.");
            return true;
        }

        /// <summary>
        /// Enables Legacy Blend Shape Normals on the source model. Without it Unity recalculates blendshape
        /// normals and merged meshes show visible shading seams where the pieces meet.
        /// </summary>
        /// <returns>True if the import setting was changed.</returns>
        public static bool EnableLegacyBlendShapeNormals(GameObject avatarRoot, Action<string> progressCallback = null)
        {
            if (avatarRoot == null) return false;

            bool changedAny = false;

            foreach (SkinnedMeshRenderer smr in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null || smr.sharedMesh.blendShapeCount == 0) continue;

                string path = AssetDatabase.GetAssetPath(smr.sharedMesh);
                if (string.IsNullOrEmpty(path)) continue;

                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;
                if (importer.importBlendShapeNormals == ModelImporterNormals.Calculate) continue;

                progressCallback?.Invoke($"Enabling Legacy Blend Shape Normals on '{importer.assetPath}'...");

                importer.importBlendShapeNormals = ModelImporterNormals.Calculate;
                AssetDatabase.WriteImportSettingsIfDirty(importer.assetPath);
                importer.SaveAndReimport();

                Debug.Log($"[AvatarRigOptimizer] Enabled Legacy Blend Shape Normals on '{importer.assetPath}'. Note: this affects every avatar using that model.");
                changedAny = true;
            }

            if (!changedAny)
                Debug.Log("[AvatarRigOptimizer] No model needed a Legacy Blend Shape Normals change.");

            return changedAny;
        }

        private static ModelImporter GetModelImporter(Avatar avatar)
        {
            if (avatar == null) return null;
            string path = AssetDatabase.GetAssetPath(avatar);
            if (string.IsNullOrEmpty(path)) return null;
            return AssetImporter.GetAtPath(path) as ModelImporter;
        }
    }
}
