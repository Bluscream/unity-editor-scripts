using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Dedicated optimizer for combining static and skinned meshes to optimize avatar mesh count.
    /// </summary>
    public static class AvatarMeshCountOptimizer
    {
        /// <summary>
        /// Combines static MeshRenderer components sharing materials into single combined meshes to fit within profile limits.
        /// </summary>
        public static void OptimizeMeshCount(GameObject avatarRoot, int maxSkinnedMeshes, int maxMeshRenderers, Action<string> progressCallback = null)
        {
            if (avatarRoot == null) return;

            MeshRenderer[] meshRenderers = avatarRoot.GetComponentsInChildren<MeshRenderer>(true);
            if (meshRenderers.Length <= 1) return;

            progressCallback?.Invoke("Combining static meshes to optimize mesh count...");

            var groups = meshRenderers
                .Where(mr => mr != null && mr.enabled && mr.GetComponent<MeshFilter>() != null && mr.GetComponent<MeshFilter>().sharedMesh != null)
                .GroupBy(mr => mr.transform.parent)
                .ToList();

            foreach (var group in groups)
            {
                var renderersList = group.ToList();
                if (renderersList.Count <= 1) continue;

                Transform parent = group.Key != null ? group.Key : avatarRoot.transform;
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
                            CombineInstance ci = new CombineInstance
                            {
                                mesh = mf.sharedMesh,
                                subMeshIndex = sub,
                                transform = avatarRoot.transform.worldToLocalMatrix * mr.transform.localToWorldMatrix
                            };
                            combineInstances.Add(ci);
                            combinedMaterials.Add(mr.sharedMaterials[sub]);
                        }
                    }
                }

                if (combineInstances.Count > 0)
                {
                    GameObject combinedGo = new GameObject("Combined_Static_Mesh");
                    combinedGo.transform.SetParent(avatarRoot.transform, false);

                    MeshFilter combinedMf = combinedGo.AddComponent<MeshFilter>();
                    MeshRenderer combinedMr = combinedGo.AddComponent<MeshRenderer>();

                    Mesh combinedMesh = new Mesh();
                    combinedMesh.name = "CombinedStaticMesh";
                    combinedMesh.CombineMeshes(combineInstances.ToArray(), false, true);

                    combinedMf.sharedMesh = combinedMesh;
                    combinedMr.sharedMaterials = combinedMaterials.ToArray();

                    foreach (MeshRenderer mr in renderersList)
                    {
                        UnityEngine.Object.DestroyImmediate(mr.gameObject);
                    }

                    Debug.Log($"[AvatarMeshCountOptimizer] Successfully combined {renderersList.Count} static MeshRenderers into '{combinedGo.name}'.");
                }
            }
        }
    }
}
