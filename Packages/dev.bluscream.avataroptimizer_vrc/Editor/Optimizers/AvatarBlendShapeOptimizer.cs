using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Bakes non-animated blendshapes into base mesh geometry and strips unused blendshapes to save VRAM,
    /// while strictly whitelisting MMD facial morphs and VRC visemes/blinks.
    /// </summary>
    public static class AvatarBlendShapeOptimizer
    {
        private static readonly HashSet<string> MmdBlendShapeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "vrc.v_sil", "vrc.v_pp", "vrc.v_ff", "vrc.v_th", "vrc.v_dd", "vrc.v_kk", "vrc.v_ch",
            "vrc.v_ss", "vrc.v_nn", "vrc.v_rr", "vrc.v_aa", "vrc.v_e", "vrc.v_ih", "vrc.v_oh", "vrc.v_ou",
            "vrc.blink_left", "vrc.blink_right", "blink", "a", "i", "u", "e", "o",
            "mth_a", "mth_i", "mth_u", "mth_e", "mth_o", "eye_blink", "eye_smile"
        };

        public static void OptimizeBlendShapes(GameObject avatarRoot, bool keepMMD = true, Action<string> progressCallback = null)
        {
            if (avatarRoot == null) return;

            // Step 1: Collect all animated blendshape names across all AnimatorControllers
            HashSet<string> usedBlendShapes = CollectUsedBlendShapes(avatarRoot);

            SkinnedMeshRenderer[] smrs = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int totalBaked = 0;
            int totalStripped = 0;

            foreach (var smr in smrs)
            {
                if (smr == null || smr.sharedMesh == null) continue;
                Mesh mesh = smr.sharedMesh;
                int shapeCount = mesh.blendShapeCount;
                if (shapeCount == 0) continue;

                List<int> shapesToBakeOrStrip = new List<int>();
                for (int i = 0; i < shapeCount; i++)
                {
                    string shapeName = mesh.GetBlendShapeName(i);

                    // Skip MMD blendshapes if configured
                    if (keepMMD && (MmdBlendShapeNames.Contains(shapeName) || shapeName.StartsWith("vrc.", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // Skip blendshapes referenced in animation clips
                    if (usedBlendShapes.Contains(shapeName))
                        continue;

                    shapesToBakeOrStrip.Add(i);
                }

                if (shapesToBakeOrStrip.Count == 0) continue;

                progressCallback?.Invoke($"Optimizing blendshapes on '{smr.gameObject.name}' ({shapesToBakeOrStrip.Count} unanimated)...");
                OptimizerLog.Verbose("AvatarBlendShapeOptimizer",
                    $"'{smr.gameObject.name}': {shapeCount} shape(s), {shapesToBakeOrStrip.Count} unanimated -> " +
                    $"{string.Join(", ", shapesToBakeOrStrip.Select(i => mesh.GetBlendShapeName(i)).Take(10))}" +
                    $"{(shapesToBakeOrStrip.Count > 10 ? $" (+{shapesToBakeOrStrip.Count - 10} more)" : "")}");

                BakeAndStripBlendShapes(smr, shapesToBakeOrStrip, ref totalBaked, ref totalStripped);

                // Baking rewrites vertex positions and rebuilds the remaining shapes.
                MeshIntegrity.Validate(smr.sharedMesh, $"blendshape bake on '{smr.gameObject.name}'", smr);
            }

            Debug.Log($"[AvatarBlendShapeOptimizer] Complete: {totalBaked} blendshape(s) baked into geometry, {totalStripped} unused shape(s) stripped.");
        }

        private static HashSet<string> CollectUsedBlendShapes(GameObject avatarRoot)
        {
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Animator[] animators = avatarRoot.GetComponentsInChildren<Animator>(true);
            foreach (var anim in animators)
            {
                if (anim == null || anim.runtimeAnimatorController == null) continue;
                AnimationClip[] clips = anim.runtimeAnimatorController.animationClips;
                if (clips == null) continue;

                foreach (var clip in clips)
                {
                    if (clip == null) continue;
                    EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
                    foreach (var binding in bindings)
                    {
                        if (binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape."))
                        {
                            string shapeName = binding.propertyName.Substring("blendShape.".Length);
                            used.Add(shapeName);
                        }
                    }
                }
            }

            return used;
        }

        private static void BakeAndStripBlendShapes(SkinnedMeshRenderer smr, List<int> shapeIndices, ref int bakedCount, ref int strippedCount)
        {
            Mesh mesh = smr.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector3[] tangents = mesh.tangents.Select(t => (Vector3)t).ToArray();
            int vertexCount = mesh.vertexCount;

            HashSet<int> indicesToRemove = new HashSet<int>(shapeIndices);

            // Step 1: Bake shapes with weight > 0 into base vertex positions
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                if (!indicesToRemove.Contains(i)) continue;

                float weight = smr.GetBlendShapeWeight(i);
                if (weight > 0.001f)
                {
                    float factor = weight / 100.0f;
                    int frameCount = mesh.GetBlendShapeFrameCount(i);
                    if (frameCount > 0)
                    {
                        Vector3[] deltaVerts = new Vector3[vertexCount];
                        Vector3[] deltaNormals = new Vector3[vertexCount];
                        Vector3[] deltaTangents = new Vector3[vertexCount];

                        mesh.GetBlendShapeFrameVertices(i, frameCount - 1, deltaVerts, deltaNormals, deltaTangents);

                        for (int v = 0; v < vertexCount; v++)
                        {
                            vertices[v] += deltaVerts[v] * factor;
                            if (normals != null && normals.Length == vertexCount) normals[v] += deltaNormals[v] * factor;
                        }

                        bakedCount++;
                    }
                }
                else
                {
                    strippedCount++;
                }
            }

            // Create optimized copy mesh with un-baked/whitelisted blendshapes only
            Mesh newMesh = UnityEngine.Object.Instantiate(mesh);
            newMesh.name = $"{mesh.name}_OptimizedBlendShapes";
            newMesh.vertices = vertices;
            if (normals != null && normals.Length == vertexCount) newMesh.normals = normals;

            newMesh.ClearBlendShapes();

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                if (indicesToRemove.Contains(i)) continue;

                string shapeName = mesh.GetBlendShapeName(i);
                int frameCount = mesh.GetBlendShapeFrameCount(i);

                for (int f = 0; f < frameCount; f++)
                {
                    float frameWeight = mesh.GetBlendShapeFrameWeight(i, f);
                    Vector3[] deltaVerts = new Vector3[vertexCount];
                    Vector3[] deltaNormals = new Vector3[vertexCount];
                    Vector3[] deltaTangents = new Vector3[vertexCount];

                    mesh.GetBlendShapeFrameVertices(i, f, deltaVerts, deltaNormals, deltaTangents);
                    newMesh.AddBlendShapeFrame(shapeName, frameWeight, deltaVerts, deltaNormals, deltaTangents);
                }
            }

            Undo.RecordObject(smr, "Bake and Strip BlendShapes");
            smr.sharedMesh = newMesh;
        }
    }
}
