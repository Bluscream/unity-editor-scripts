using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Dedicated optimizer for combining static meshes to reduce avatar MeshRenderer count.
    /// </summary>
    public static class AvatarMeshCountOptimizer
    {
        /// <summary>
        /// Combines static MeshRenderers sharing the same parent into single combined meshes,
        /// but only when the avatar exceeds profile limits, and only as much as needed.
        /// Combined meshes are persisted as assets; original GameObjects are kept (only their
        /// MeshRenderer/MeshFilter components are removed) so children, PhysBones, and other
        /// components survive.
        /// </summary>
        public static void OptimizeMeshCount(GameObject avatarRoot, int maxSkinnedMeshes, int maxMeshRenderers, string assetOutputDirectory = null, Action<string> progressCallback = null)
        {
            if (avatarRoot == null) return;

            int skinnedCount = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Count(r => r != null);
            if (maxSkinnedMeshes < int.MaxValue && skinnedCount > maxSkinnedMeshes)
            {
                // Combining skinned meshes (bone remapping + blendshape merging) is out of scope here.
                Debug.LogWarning($"[AvatarMeshCountOptimizer] Avatar has {skinnedCount} SkinnedMeshRenderers (limit {maxSkinnedMeshes}). Skinned mesh combining is not automated — merge them manually or with a dedicated tool.");
            }

            MeshRenderer[] meshRenderers = avatarRoot.GetComponentsInChildren<MeshRenderer>(true);
            int rendererCount = meshRenderers.Count(r => r != null);
            if (maxMeshRenderers == int.MaxValue || rendererCount <= maxMeshRenderers)
            {
                return; // already within budget — don't touch anything
            }

            progressCallback?.Invoke($"Combining static meshes to reduce MeshRenderer count ({rendererCount} -> {maxMeshRenderers})...");
            Debug.Log($"[AvatarMeshCountOptimizer] MeshRenderers {rendererCount} > limit {maxMeshRenderers}. Combining per-parent groups (largest first).");

            // Only combine renderers that are active and enabled: disabled ones are commonly
            // animator-driven toggles and must keep their own renderer to stay toggleable.
            var groups = meshRenderers
                .Where(mr => mr != null && mr.enabled && mr.gameObject.activeInHierarchy
                          && mr.GetComponent<MeshFilter>() != null && mr.GetComponent<MeshFilter>().sharedMesh != null)
                .GroupBy(mr => mr.transform.parent)
                .Where(g => g.Key != null && g.Count() > 1)
                .OrderByDescending(g => g.Count())
                .ToList();

            foreach (var group in groups)
            {
                if (rendererCount <= maxMeshRenderers) break;

                var renderersList = group.ToList();
                Transform parent = group.Key;

                List<CombineInstance> combineInstances = new List<CombineInstance>();
                List<Material> combinedMaterials = new List<Material>();

                foreach (MeshRenderer mr in renderersList)
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;

                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        if (sub < mr.sharedMaterials.Length && mr.sharedMaterials[sub] != null)
                        {
                            combineInstances.Add(new CombineInstance
                            {
                                mesh = mf.sharedMesh,
                                subMeshIndex = sub,
                                // Bake relative to the shared parent so the combined mesh still follows
                                // the parent when it is animated.
                                transform = parent.worldToLocalMatrix * mr.transform.localToWorldMatrix
                            });
                            combinedMaterials.Add(mr.sharedMaterials[sub]);
                        }
                    }
                }

                if (combineInstances.Count == 0) continue;

                Mesh combinedMesh = new Mesh();
                combinedMesh.name = $"{parent.name}_Combined";
                combinedMesh.CombineMeshes(combineInstances.ToArray(), false, true);

                // Persist the combined mesh — an unsaved scene mesh would be lost on editor restart.
                string savedPath = SaveMeshAsset(combinedMesh, avatarRoot.name, assetOutputDirectory);

                GameObject combinedGo = new GameObject($"Combined_{parent.name}");
                Undo.RegisterCreatedObjectUndo(combinedGo, "Combine Static Meshes");
                combinedGo.transform.SetParent(parent, false);

                MeshFilter combinedMf = combinedGo.AddComponent<MeshFilter>();
                MeshRenderer combinedMr = combinedGo.AddComponent<MeshRenderer>();
                combinedMf.sharedMesh = combinedMesh;
                combinedMr.sharedMaterials = combinedMaterials.ToArray();

                // Remove only the renderer components — keep the GameObjects and everything else on them.
                foreach (MeshRenderer mr in renderersList)
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    Undo.DestroyObjectImmediate(mr);
                    if (mf != null) Undo.DestroyObjectImmediate(mf);
                }

                rendererCount -= renderersList.Count - 1;
                Debug.Log($"[AvatarMeshCountOptimizer] Combined {renderersList.Count} static MeshRenderers under '{parent.name}' into '{combinedGo.name}'{(savedPath != null ? $" (mesh saved: {savedPath})" : "")}. Renderer count now {rendererCount}.");
            }

            if (rendererCount > maxMeshRenderers)
            {
                Debug.LogWarning($"[AvatarMeshCountOptimizer] Could not reach MeshRenderer limit: {rendererCount} / {maxMeshRenderers} (remaining renderers have no combinable same-parent group).");
            }
        }

        private static string SaveMeshAsset(Mesh mesh, string avatarName, string assetOutputDirectory)
        {
            try
            {
                string dir = !string.IsNullOrEmpty(assetOutputDirectory)
                    ? assetOutputDirectory
                    : "Assets/_AVATAROPTIMIZER/" + avatarName;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{mesh.name}.asset".Replace('\\', '/'));
                AssetDatabase.CreateAsset(mesh, path);
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarMeshCountOptimizer] Could not persist combined mesh '{mesh.name}' as asset: {e.Message}");
                return null;
            }
        }
    }
}
