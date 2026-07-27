using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Dedicated optimizer for deduplicating materials and consolidating material slots across avatar renderers.
    /// </summary>
    public static class AvatarMaterialSlotOptimizer
    {
        /// <summary>
        /// Deduplicates materials, consolidates material slots, and merges duplicate submesh indices
        /// on renderers to fit within profile.MaxMaterialSlots.
        /// </summary>
        public static void OptimizeMaterialSlots(GameObject avatarRoot, int maxMaterialSlots, Action<string> progressCallback = null)
        {
            if (avatarRoot == null || maxMaterialSlots >= 1000) return;

            progressCallback?.Invoke("Optimizing and consolidating material slots...");
            Renderer[] renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            int initialSlots = renderers.Sum(r => r != null && r.sharedMaterials != null ? r.sharedMaterials.Length : 0);

            // Step 1: Consolidate duplicate material entries on individual renderers
            foreach (Renderer r in renderers)
            {
                if (r == null || r.sharedMaterials == null || r.sharedMaterials.Length <= 1) continue;

                Material[] mats = r.sharedMaterials;
                List<Material> uniqueMats = new List<Material>();
                List<int> remapIndex = new List<int>();

                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null) continue;

                    int existingIdx = uniqueMats.FindIndex(u => u == m || IsIdenticalMaterial(u, m));
                    if (existingIdx >= 0)
                    {
                        remapIndex.Add(existingIdx);
                    }
                    else
                    {
                        remapIndex.Add(uniqueMats.Count);
                        uniqueMats.Add(m);
                    }
                }

                if (uniqueMats.Count < mats.Length)
                {
                    Undo.RecordObject(r, "Consolidate Material Slots");
                    Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : (r is MeshRenderer mr && mr.GetComponent<MeshFilter>() != null ? mr.GetComponent<MeshFilter>().sharedMesh : null);

                    if (mesh != null && mesh.subMeshCount == mats.Length)
                    {
                        // Create consolidated mesh with combined submeshes
                        Mesh newMesh = UnityEngine.Object.Instantiate(mesh);
                        newMesh.name = mesh.name + "_Consolidated";
                        List<List<int>> newSubmeshTris = uniqueMats.Select(_ => new List<int>()).ToList();

                        for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        {
                            int targetSubIdx = remapIndex[sub];
                            int[] subTris = mesh.GetTriangles(sub);
                            newSubmeshTris[targetSubIdx].AddRange(subTris);
                        }

                        newMesh.subMeshCount = uniqueMats.Count;
                        for (int targetSub = 0; targetSub < uniqueMats.Count; targetSub++)
                        {
                            newMesh.SetTriangles(newSubmeshTris[targetSub].ToArray(), targetSub);
                        }

                        if (r is SkinnedMeshRenderer sRenderer) sRenderer.sharedMesh = newMesh;
                        else if (r is MeshRenderer mRenderer) mRenderer.GetComponent<MeshFilter>().sharedMesh = newMesh;
                    }

                    r.sharedMaterials = uniqueMats.ToArray();
                }
            }

            int finalSlots = avatarRoot.GetComponentsInChildren<Renderer>(true).Sum(r => r != null && r.sharedMaterials != null ? r.sharedMaterials.Length : 0);
            Debug.Log($"[AvatarMaterialSlotOptimizer] Material Slot Consolidation complete: {initialSlots} slots -> {finalSlots} slots.");
        }

        private static bool IsIdenticalMaterial(Material a, Material b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;
            if (a.shader != b.shader) return false;
            if (a.mainTexture != b.mainTexture) return false;
            if (a.color != b.color) return false;
            return true;
        }
    }
}
