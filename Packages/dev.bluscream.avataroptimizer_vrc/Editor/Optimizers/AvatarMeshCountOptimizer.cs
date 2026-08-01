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

            var skinnedRenderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int skinnedCount = skinnedRenderers.Count(r => r != null);
            if (maxSkinnedMeshes < int.MaxValue && skinnedCount > maxSkinnedMeshes)
            {
                progressCallback?.Invoke($"Combining Skinned Mesh Renderers ({skinnedCount} -> max {maxSkinnedMeshes})...");
                CombineSkinnedMeshes(avatarRoot, skinnedRenderers, maxSkinnedMeshes, assetOutputDirectory);
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

                // Group combineInstances by Material to merge submeshes sharing the same material
                var staticMaterialGroups = combineInstances
                    .Select((ci, idx) => new { Instance = ci, Material = combinedMaterials[idx] })
                    .GroupBy(x => x.Material)
                    .ToList();

                List<CombineInstance> consolidatedStaticList = new List<CombineInstance>();
                List<Material> finalStaticMaterialsList = new List<Material>();

                foreach (var matGroup in staticMaterialGroups)
                {
                    Material mat = matGroup.Key;
                    var instances = matGroup.Select(x => x.Instance).ToArray();

                    Mesh subMeshGroup = new Mesh();
                    subMeshGroup.CombineMeshes(instances, true, true);

                    consolidatedStaticList.Add(new CombineInstance
                    {
                        mesh = subMeshGroup,
                        subMeshIndex = 0,
                        transform = Matrix4x4.identity
                    });
                    finalStaticMaterialsList.Add(mat);
                }

                Mesh combinedMesh = new Mesh();
                combinedMesh.name = $"{parent.name}_Combined";
                combinedMesh.CombineMeshes(consolidatedStaticList.ToArray(), false, true);

                // Persist the combined mesh — an unsaved scene mesh would be lost on editor restart.
                string savedPath = SaveMeshAsset(combinedMesh, avatarRoot.name, assetOutputDirectory);

                GameObject combinedGo = new GameObject($"Combined_{parent.name}");
                Undo.RegisterCreatedObjectUndo(combinedGo, "Combine Static Meshes");
                combinedGo.transform.SetParent(parent, false);

                MeshFilter combinedMf = combinedGo.AddComponent<MeshFilter>();
                MeshRenderer combinedMr = combinedGo.AddComponent<MeshRenderer>();
                combinedMf.sharedMesh = combinedMesh;
                combinedMr.sharedMaterials = finalStaticMaterialsList.ToArray();

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

        private static void CombineSkinnedMeshes(GameObject avatarRoot, SkinnedMeshRenderer[] smrs, int maxSkinnedMeshes, string assetOutputDirectory)
        {
            var activeSmrs = smrs.Where(s => s != null && s.enabled && s.gameObject.activeInHierarchy && s.sharedMesh != null && !AvatarPenetratorDetector.IsPenetratorRenderer(s)).ToList();
            if (activeSmrs.Count <= maxSkinnedMeshes) return;

            // Prefer not to merge the main body/face renderer or DPS/SPS penetrators.
            var candidatesToMerge = activeSmrs
                .Where(s => s.name.IndexOf("body", StringComparison.OrdinalIgnoreCase) != 0
                         && s.name.IndexOf("face", StringComparison.OrdinalIgnoreCase) != 0
                         && !AvatarPenetratorDetector.IsPenetratorRenderer(s))
                .ToList();

            if (candidatesToMerge.Count < 2) candidatesToMerge = activeSmrs;

            int targetMergeCount = activeSmrs.Count - maxSkinnedMeshes + 1;
            var groupToMerge = candidatesToMerge.Take(targetMergeCount).ToList();
            if (groupToMerge.Count < 2) return;

            // The mesh is assembled by hand rather than via Mesh.CombineMeshes. CombineMeshes drops
            // blendshapes entirely and gives no control over the final vertex order, which is what
            // previously left bone weights misaligned with their vertices. Building the buffers directly
            // makes the vertex order explicit, so weights and blendshape deltas line up by construction.

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector2>();
            var colors = new List<Color>();
            var boneWeights = new List<BoneWeight>();

            var allBones = new List<Transform>();
            var bindPoses = new List<Matrix4x4>();
            // Bones are only shared between meshes when their bindpose matches too — the same Transform
            // with a different bindpose is a different space and must stay a separate entry.
            var boneLookup = new Dictionary<(Transform, Matrix4x4), int>();

            // Triangles per material, in the combined mesh's index space.
            var trianglesByMaterial = new Dictionary<Material, List<int>>();
            var materialOrder = new List<Material>();

            // Where each source mesh's vertices start in the combined buffers, for blendshape transfer.
            var vertexOffsets = new List<int>();
            var sourceMeshes = new List<Mesh>();

            Transform rootBone = groupToMerge[0].rootBone != null ? groupToMerge[0].rootBone : avatarRoot.transform;

            foreach (SkinnedMeshRenderer smr in groupToMerge)
            {
                Mesh mesh = smr.sharedMesh;
                if (mesh == null) continue;

                int vertexOffset = vertices.Count;
                vertexOffsets.Add(vertexOffset);
                sourceMeshes.Add(mesh);

                int vertexCount = mesh.vertexCount;

                Vector3[] srcVertices = mesh.vertices;
                Vector3[] srcNormals = mesh.normals;
                Vector4[] srcTangents = mesh.tangents;
                Vector2[] srcUv0 = mesh.uv;
                Vector2[] srcUv1 = mesh.uv2;
                Color[] srcColors = mesh.colors;

                vertices.AddRange(srcVertices);
                for (int v = 0; v < vertexCount; v++)
                {
                    normals.Add(srcNormals != null && v < srcNormals.Length ? srcNormals[v] : Vector3.up);
                    tangents.Add(srcTangents != null && v < srcTangents.Length ? srcTangents[v] : new Vector4(1, 0, 0, -1));
                    uv0.Add(srcUv0 != null && v < srcUv0.Length ? srcUv0[v] : Vector2.zero);
                    uv1.Add(srcUv1 != null && v < srcUv1.Length ? srcUv1[v] : Vector2.zero);
                    colors.Add(srcColors != null && v < srcColors.Length ? srcColors[v] : Color.white);
                }

                // Map this mesh's bones into the shared bone list.
                Transform[] smrBones = smr.bones != null && smr.bones.Length > 0 ? smr.bones : new[] { smr.transform };
                Matrix4x4[] smrBindPoses = mesh.bindposes != null && mesh.bindposes.Length == smrBones.Length
                    ? mesh.bindposes
                    : smrBones.Select(b => b != null ? b.worldToLocalMatrix * smr.transform.localToWorldMatrix : Matrix4x4.identity).ToArray();

                var boneMap = new int[smrBones.Length];
                for (int b = 0; b < smrBones.Length; b++)
                {
                    Transform bone = smrBones[b] != null ? smrBones[b] : smr.transform;
                    Matrix4x4 bindPose = smrBindPoses[b];

                    var key = (bone, bindPose);
                    if (!boneLookup.TryGetValue(key, out int index))
                    {
                        index = allBones.Count;
                        allBones.Add(bone);
                        bindPoses.Add(bindPose);
                        boneLookup[key] = index;
                    }
                    boneMap[b] = index;
                }

                // Bone weights, remapped onto the shared bone indices.
                BoneWeight[] srcWeights = mesh.boneWeights;
                for (int v = 0; v < vertexCount; v++)
                {
                    if (srcWeights != null && v < srcWeights.Length)
                    {
                        BoneWeight bw = srcWeights[v];
                        if (bw.weight0 > 0) bw.boneIndex0 = boneMap[Mathf.Clamp(bw.boneIndex0, 0, boneMap.Length - 1)];
                        if (bw.weight1 > 0) bw.boneIndex1 = boneMap[Mathf.Clamp(bw.boneIndex1, 0, boneMap.Length - 1)];
                        if (bw.weight2 > 0) bw.boneIndex2 = boneMap[Mathf.Clamp(bw.boneIndex2, 0, boneMap.Length - 1)];
                        if (bw.weight3 > 0) bw.boneIndex3 = boneMap[Mathf.Clamp(bw.boneIndex3, 0, boneMap.Length - 1)];
                        boneWeights.Add(bw);
                    }
                    else
                    {
                        // Unskinned vertex: bind it rigidly to this mesh's first bone.
                        boneWeights.Add(new BoneWeight { boneIndex0 = boneMap.Length > 0 ? boneMap[0] : 0, weight0 = 1f });
                    }
                }

                // Triangles, offset into the combined vertex space and bucketed by material.
                Material[] smrMaterials = smr.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Material mat = sub < smrMaterials.Length ? smrMaterials[sub] : null;
                    if (mat == null) continue;

                    if (!trianglesByMaterial.TryGetValue(mat, out List<int> triList))
                    {
                        trianglesByMaterial[mat] = triList = new List<int>();
                        materialOrder.Add(mat);
                    }

                    foreach (int idx in mesh.GetTriangles(sub))
                        triList.Add(idx + vertexOffset);
                }
            }

            if (vertices.Count == 0 || materialOrder.Count == 0) return;

            Mesh combinedMesh = new Mesh
            {
                name = $"{avatarRoot.name}_CombinedSkinnedMesh",
                indexFormat = vertices.Count > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };

            combinedMesh.SetVertices(vertices);
            combinedMesh.SetNormals(normals);
            combinedMesh.SetTangents(tangents);
            combinedMesh.SetUVs(0, uv0);
            combinedMesh.SetUVs(1, uv1);
            combinedMesh.SetColors(colors);
            combinedMesh.boneWeights = boneWeights.ToArray();
            combinedMesh.bindposes = bindPoses.ToArray();

            combinedMesh.subMeshCount = materialOrder.Count;
            for (int i = 0; i < materialOrder.Count; i++)
                combinedMesh.SetTriangles(trianglesByMaterial[materialOrder[i]], i);

            int shapesTransferred = TransferBlendShapes(combinedMesh, sourceMeshes, vertexOffsets, vertices.Count);

            combinedMesh.RecalculateBounds();

            string savedPath = SaveMeshAsset(combinedMesh, avatarRoot.name, assetOutputDirectory);

            GameObject combinedGo = new GameObject("Combined_SkinnedMesh");
            Undo.RegisterCreatedObjectUndo(combinedGo, "Combine Skinned Meshes");
            combinedGo.transform.SetParent(avatarRoot.transform, false);

            SkinnedMeshRenderer combinedSmr = combinedGo.AddComponent<SkinnedMeshRenderer>();
            combinedSmr.sharedMesh = combinedMesh;
            combinedSmr.sharedMaterials = materialOrder.ToArray();
            combinedSmr.bones = allBones.ToArray();
            combinedSmr.rootBone = rootBone;

            // Remove combined original SMR components
            foreach (var smr in groupToMerge)
            {
                Undo.DestroyObjectImmediate(smr);
            }

            Debug.Log($"[AvatarMeshCountOptimizer] Merged {groupToMerge.Count} SkinnedMeshRenderers into '{combinedGo.name}' " +
                      $"(bones: {allBones.Count}, vertices: {combinedMesh.vertexCount}, submeshes: {materialOrder.Count}, blendshapes: {shapesTransferred})" +
                      $"{(savedPath != null ? $" (mesh saved: {savedPath})" : "")}.");
        }

        /// <summary>
        /// Rebuilds every source mesh's blendshapes on the combined mesh. Each source contributes deltas
        /// only within its own vertex block and zero elsewhere, so shapes sharing a name across meshes
        /// merge into one shape that drives all of them — which is what animations targeting e.g. "blink"
        /// expect after a merge.
        /// </summary>
        private static int TransferBlendShapes(Mesh combinedMesh, List<Mesh> sourceMeshes, List<int> vertexOffsets, int totalVertices)
        {
            // shape name -> weight -> accumulated deltas across every source mesh that defines it
            var shapes = new Dictionary<string, SortedDictionary<float, (Vector3[] v, Vector3[] n, Vector3[] t)>>(StringComparer.Ordinal);
            var shapeOrder = new List<string>();

            for (int m = 0; m < sourceMeshes.Count; m++)
            {
                Mesh src = sourceMeshes[m];
                if (src == null || src.blendShapeCount == 0) continue;

                int offset = vertexOffsets[m];
                int srcVertexCount = src.vertexCount;

                var dv = new Vector3[srcVertexCount];
                var dn = new Vector3[srcVertexCount];
                var dt = new Vector3[srcVertexCount];

                for (int s = 0; s < src.blendShapeCount; s++)
                {
                    string name = src.GetBlendShapeName(s);
                    if (!shapes.TryGetValue(name, out var frames))
                    {
                        shapes[name] = frames = new SortedDictionary<float, (Vector3[], Vector3[], Vector3[])>();
                        shapeOrder.Add(name);
                    }

                    int frameCount = src.GetBlendShapeFrameCount(s);
                    for (int f = 0; f < frameCount; f++)
                    {
                        float weight = src.GetBlendShapeFrameWeight(s, f);
                        src.GetBlendShapeFrameVertices(s, f, dv, dn, dt);

                        if (!frames.TryGetValue(weight, out var acc))
                        {
                            acc = (new Vector3[totalVertices], new Vector3[totalVertices], new Vector3[totalVertices]);
                            frames[weight] = acc;
                        }

                        for (int v = 0; v < srcVertexCount; v++)
                        {
                            acc.v[offset + v] = dv[v];
                            acc.n[offset + v] = dn[v];
                            acc.t[offset + v] = dt[v];
                        }
                    }
                }
            }

            foreach (string name in shapeOrder)
            {
                foreach (var frame in shapes[name])
                    combinedMesh.AddBlendShapeFrame(name, frame.Key, frame.Value.v, frame.Value.n, frame.Value.t);
            }

            return shapeOrder.Count;
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
