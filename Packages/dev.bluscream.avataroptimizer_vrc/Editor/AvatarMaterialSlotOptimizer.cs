using System;
using System.Collections.Generic;
using System.IO;
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
        /// on renderers. Null slots are dropped together with their submesh triangles.
        /// </summary>
        public static void OptimizeMaterialSlots(GameObject avatarRoot, int maxMaterialSlots, string assetOutputDirectory = null, Action<string> progressCallback = null)
        {
            if (avatarRoot == null || maxMaterialSlots == int.MaxValue) return;

            progressCallback?.Invoke("Optimizing and consolidating material slots...");
            Renderer[] renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            int initialSlots = renderers.Sum(r => r != null && r.sharedMaterials != null ? r.sharedMaterials.Length : 0);

            // Consolidate duplicate material entries on individual renderers
            foreach (Renderer r in renderers)
            {
                if (r == null || r.sharedMaterials == null || r.sharedMaterials.Length <= 1) continue;

                Material[] mats = r.sharedMaterials;
                List<Material> uniqueMats = new List<Material>();
                // remapIndex is aligned with the ORIGINAL slot indices; -1 marks a dropped (null) slot.
                int[] remapIndex = new int[mats.Length];

                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null)
                    {
                        remapIndex[i] = -1;
                        continue;
                    }

                    int existingIdx = uniqueMats.FindIndex(u => u == m || IsIdenticalMaterial(u, m));
                    if (existingIdx >= 0)
                    {
                        remapIndex[i] = existingIdx;
                    }
                    else
                    {
                        remapIndex[i] = uniqueMats.Count;
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
                            if (targetSubIdx < 0) continue; // null slot — drop its triangles
                            newSubmeshTris[targetSubIdx].AddRange(mesh.GetTriangles(sub));
                        }

                        newMesh.subMeshCount = uniqueMats.Count;
                        for (int targetSub = 0; targetSub < uniqueMats.Count; targetSub++)
                        {
                            newMesh.SetTriangles(newSubmeshTris[targetSub].ToArray(), targetSub);
                        }

                        // Persist — an unsaved scene mesh would be lost on editor restart.
                        SaveMeshAsset(newMesh, avatarRoot.name, assetOutputDirectory);

                        if (r is SkinnedMeshRenderer sRenderer) sRenderer.sharedMesh = newMesh;
                        else if (r is MeshRenderer mRenderer) mRenderer.GetComponent<MeshFilter>().sharedMesh = newMesh;

                        r.sharedMaterials = uniqueMats.ToArray();
                        Debug.Log($"[AvatarMaterialSlotOptimizer] Consolidated '{r.name}': {mats.Length} slots -> {uniqueMats.Count}.");
                    }
                    else if (mesh == null)
                    {
                        // No mesh to remap (e.g. TrailRenderer/LineRenderer material lists) — safe to just dedupe the array
                        r.sharedMaterials = uniqueMats.ToArray();
                    }
                    // If subMeshCount != slot count, leave the renderer alone: reassigning the material
                    // array without remapping submeshes would change which submesh gets which material.
                }
            }

            int finalSlots = avatarRoot.GetComponentsInChildren<Renderer>(true).Sum(r => r != null && r.sharedMaterials != null ? r.sharedMaterials.Length : 0);
            Debug.Log($"[AvatarMaterialSlotOptimizer] Material Slot Consolidation complete: {initialSlots} slots -> {finalSlots} slots.");

            if (finalSlots > maxMaterialSlots)
            {
                Debug.LogWarning($"[AvatarMaterialSlotOptimizer] Avatar still has {finalSlots} material slots (limit {maxMaterialSlots}). Deduplication alone cannot reach the limit — distinct materials would need atlasing/merging.");
            }
        }

        /// <summary>
        /// True if both materials use the same shader and have identical values for every shader property.
        /// </summary>
        private static bool IsIdenticalMaterial(Material a, Material b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;
            if (a.shader != b.shader) return false;
            if (a.renderQueue != b.renderQueue) return false;
            if (!a.shaderKeywords.OrderBy(k => k).SequenceEqual(b.shaderKeywords.OrderBy(k => k))) return false;

            Shader shader = a.shader;
            int propertyCount = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < propertyCount; i++)
            {
                string prop = ShaderUtil.GetPropertyName(shader, i);
                switch (ShaderUtil.GetPropertyType(shader, i))
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        if (a.GetColor(prop) != b.GetColor(prop)) return false;
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        if (a.GetVector(prop) != b.GetVector(prop)) return false;
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        if (!Mathf.Approximately(a.GetFloat(prop), b.GetFloat(prop))) return false;
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        if (a.GetTexture(prop) != b.GetTexture(prop)) return false;
                        if (a.GetTextureScale(prop) != b.GetTextureScale(prop)) return false;
                        if (a.GetTextureOffset(prop) != b.GetTextureOffset(prop)) return false;
                        break;
                }
            }
            return true;
        }

        private static void SaveMeshAsset(Mesh mesh, string avatarName, string assetOutputDirectory)
        {
            try
            {
                string dir = !string.IsNullOrEmpty(assetOutputDirectory)
                    ? assetOutputDirectory
                    : "Assets/_AVATAROPTIMIZER/" + avatarName;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{mesh.name}.asset".Replace('\\', '/'));
                AssetDatabase.CreateAsset(mesh, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarMaterialSlotOptimizer] Could not persist consolidated mesh '{mesh.name}' as asset: {e.Message}");
            }
        }
    }
}
