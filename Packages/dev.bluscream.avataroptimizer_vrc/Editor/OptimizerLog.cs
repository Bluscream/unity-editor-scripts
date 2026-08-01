using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public enum OptimizerLogLevel
    {
        /// <summary>Warnings and errors only.</summary>
        Quiet = 0,
        /// <summary>Step boundaries and per-pass results. The default.</summary>
        Normal = 1,
        /// <summary>Per-object decisions: what was merged, skipped, kept, and why.</summary>
        Verbose = 2,
        /// <summary>Per-vertex-block / per-element detail. Very noisy; for diagnosing a specific bad avatar.</summary>
        Trace = 3
    }

    /// <summary>
    /// Verbosity-gated logging for the optimizer.
    ///
    /// The passes that rewrite vertex data fail silently when they go wrong — a bone index pointing at the
    /// wrong bone or a UV landing in the wrong atlas cell produces a mesh that loads fine and renders
    /// wrongly. <see cref="MeshIntegrity"/> exists to make those failures loud at the point they are
    /// introduced rather than in-game.
    /// </summary>
    public static class OptimizerLog
    {
        private const string LevelPref = "VRCAvatarOptimizer_LogLevel";
        private const string ValidatePref = "VRCAvatarOptimizer_ValidateMeshes";

        public static OptimizerLogLevel Level
        {
            get => (OptimizerLogLevel)EditorPrefs.GetInt(LevelPref, (int)OptimizerLogLevel.Normal);
            set => EditorPrefs.SetInt(LevelPref, (int)value);
        }

        /// <summary>Run mesh integrity checks after every pass that rewrites vertex data.</summary>
        public static bool ValidateMeshes
        {
            get => EditorPrefs.GetBool(ValidatePref, true);
            set => EditorPrefs.SetBool(ValidatePref, value);
        }

        public static bool IsVerbose => Level >= OptimizerLogLevel.Verbose;
        public static bool IsTrace => Level >= OptimizerLogLevel.Trace;

        public static void Info(string tag, string message)
        {
            if (Level >= OptimizerLogLevel.Normal) Debug.Log($"[{tag}] {message}");
        }

        public static void Verbose(string tag, string message)
        {
            if (Level >= OptimizerLogLevel.Verbose) Debug.Log($"[{tag}] {message}");
        }

        /// <summary>
        /// Trace messages are lazily built — the string is never constructed unless Trace is enabled, so
        /// per-vertex logging costs nothing at lower levels.
        /// </summary>
        public static void Trace(string tag, Func<string> message)
        {
            if (Level >= OptimizerLogLevel.Trace && message != null) Debug.Log($"[{tag}] {message()}");
        }

        public static void Warn(string tag, string message, UnityEngine.Object context = null)
        {
            if (context != null) Debug.LogWarning($"[{tag}] {message}", context);
            else Debug.LogWarning($"[{tag}] {message}");
        }

        public static void Error(string tag, string message, UnityEngine.Object context = null)
        {
            if (context != null) Debug.LogError($"[{tag}] {message}", context);
            else Debug.LogError($"[{tag}] {message}");
        }
    }

    /// <summary>
    /// Structural checks for meshes the optimizer generates.
    ///
    /// Every check here targets a failure mode that Unity accepts without complaint but that renders
    /// incorrectly: bone indices out of range, weights that do not sum to 1, triangle indices past the end
    /// of the vertex buffer, NaN geometry, or a submesh count that disagrees with the material array.
    /// </summary>
    public static class MeshIntegrity
    {
        /// <summary>Bone weights are allowed to drift this far from summing to 1 before it is reported.</summary>
        private const float WeightSumTolerance = 0.001f;

        /// <summary>How many individual offending elements to name before summarising the rest.</summary>
        private const int MaxReportedExamples = 5;

        /// <summary>
        /// Validates a generated mesh and logs anything wrong with it.
        /// </summary>
        /// <param name="renderer">Optional; enables bone-array and material-slot cross-checks.</param>
        /// <returns>True when the mesh passed every check.</returns>
        public static bool Validate(Mesh mesh, string context, Renderer renderer = null)
        {
            if (!OptimizerLog.ValidateMeshes) return true;

            const string Tag = "MeshIntegrity";

            if (mesh == null)
            {
                OptimizerLog.Error(Tag, $"{context}: mesh is null.");
                return false;
            }

            var problems = new List<string>();
            int vertexCount = mesh.vertexCount;

            if (vertexCount == 0) problems.Add("mesh has no vertices");

            CheckVertexStreams(mesh, vertexCount, problems);
            CheckGeometryFinite(mesh, problems);
            CheckTriangles(mesh, vertexCount, problems);
            CheckBoneWeights(mesh, vertexCount, renderer, problems);
            CheckBlendShapes(mesh, problems);

            if (renderer != null && mesh.subMeshCount != renderer.sharedMaterials.Length)
            {
                problems.Add($"submesh count {mesh.subMeshCount} != material slot count {renderer.sharedMaterials.Length} " +
                             "(submeshes past the end of the material array are not drawn)");
            }

            if (problems.Count == 0)
            {
                OptimizerLog.Verbose(Tag, $"{context}: OK — {vertexCount:N0} verts, {mesh.subMeshCount} submesh(es), " +
                                          $"{mesh.blendShapeCount} blendshape(s), {(mesh.bindposes?.Length ?? 0)} bindpose(s).");
                return true;
            }

            OptimizerLog.Error(Tag, $"{context}: {problems.Count} integrity problem(s) — this mesh will load but render incorrectly:\n  - "
                                    + string.Join("\n  - ", problems),
                               renderer);
            return false;
        }

        private static void CheckVertexStreams(Mesh mesh, int vertexCount, List<string> problems)
        {
            void CheckLength(string name, int length)
            {
                if (length != 0 && length != vertexCount)
                    problems.Add($"{name} has {length} entries but the mesh has {vertexCount} vertices");
            }

            CheckLength("normals", mesh.normals?.Length ?? 0);
            CheckLength("tangents", mesh.tangents?.Length ?? 0);
            CheckLength("uv", mesh.uv?.Length ?? 0);
            CheckLength("uv2", mesh.uv2?.Length ?? 0);
            CheckLength("colors", mesh.colors?.Length ?? 0);
        }

        private static void CheckGeometryFinite(Mesh mesh, List<string> problems)
        {
            Vector3[] vertices = mesh.vertices;
            if (vertices == null) return;

            var bad = new List<int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                    float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z))
                {
                    bad.Add(i);
                    if (bad.Count > MaxReportedExamples) break;
                }
            }

            if (bad.Count > 0)
            {
                // NaNimation puts NaN in a *bone scale*, never in the mesh — NaN here is a real defect.
                problems.Add($"vertex position(s) are NaN or infinite (first indices: {string.Join(", ", bad.Take(MaxReportedExamples))})");
            }
        }

        private static void CheckTriangles(Mesh mesh, int vertexCount, List<string> problems)
        {
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int[] tris;
                try
                {
                    tris = mesh.GetTriangles(sub);
                }
                catch (Exception e)
                {
                    problems.Add($"submesh {sub}: could not read triangles ({e.Message})");
                    continue;
                }

                if (tris.Length % 3 != 0)
                    problems.Add($"submesh {sub}: {tris.Length} indices is not a multiple of 3");

                int outOfRange = 0;
                int firstBad = -1;
                foreach (int idx in tris)
                {
                    if (idx < 0 || idx >= vertexCount)
                    {
                        if (firstBad < 0) firstBad = idx;
                        outOfRange++;
                    }
                }

                if (outOfRange > 0)
                {
                    problems.Add($"submesh {sub}: {outOfRange} triangle index/indices outside [0,{vertexCount}) " +
                                 $"(first: {firstBad}) — a vertex offset was applied wrongly");
                }
            }
        }

        private static void CheckBoneWeights(Mesh mesh, int vertexCount, Renderer renderer, List<string> problems)
        {
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4[] bindposes = mesh.bindposes;

            bool isSkinned = (weights != null && weights.Length > 0) || (bindposes != null && bindposes.Length > 0);
            if (!isSkinned) return;

            if (weights == null || weights.Length != vertexCount)
            {
                problems.Add($"boneWeights has {(weights?.Length ?? 0)} entries but the mesh has {vertexCount} vertices " +
                             "(weights are misaligned with their vertices — the mesh will deform incorrectly)");
                return;
            }

            int boneCount = bindposes?.Length ?? 0;
            if (renderer is SkinnedMeshRenderer smr)
            {
                int rendererBones = smr.bones?.Length ?? 0;
                if (rendererBones != boneCount)
                    problems.Add($"renderer has {rendererBones} bones but the mesh has {boneCount} bindposes — these must match 1:1");
                boneCount = Mathf.Min(boneCount == 0 ? int.MaxValue : boneCount, rendererBones == 0 ? int.MaxValue : rendererBones);
            }

            int badIndex = 0, badSum = 0;
            int firstBadIndexVertex = -1, firstBadSumVertex = -1;

            for (int v = 0; v < weights.Length; v++)
            {
                BoneWeight bw = weights[v];

                if (boneCount != int.MaxValue && boneCount > 0)
                {
                    bool anyOut =
                        (bw.weight0 > 0 && (bw.boneIndex0 < 0 || bw.boneIndex0 >= boneCount)) ||
                        (bw.weight1 > 0 && (bw.boneIndex1 < 0 || bw.boneIndex1 >= boneCount)) ||
                        (bw.weight2 > 0 && (bw.boneIndex2 < 0 || bw.boneIndex2 >= boneCount)) ||
                        (bw.weight3 > 0 && (bw.boneIndex3 < 0 || bw.boneIndex3 >= boneCount));

                    if (anyOut)
                    {
                        if (firstBadIndexVertex < 0) firstBadIndexVertex = v;
                        badIndex++;
                    }
                }

                float sum = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
                if (Mathf.Abs(sum - 1f) > WeightSumTolerance)
                {
                    if (firstBadSumVertex < 0) firstBadSumVertex = v;
                    badSum++;
                }
            }

            if (badIndex > 0)
            {
                problems.Add($"{badIndex} vertex/vertices reference a bone index outside [0,{boneCount}) " +
                             $"(first at vertex {firstBadIndexVertex}) — a bone remap is wrong");
            }

            if (badSum > 0)
            {
                // NaNimation deliberately adds a fourth influence at weight 0, which does not change the sum.
                problems.Add($"{badSum} vertex/vertices have bone weights not summing to 1 " +
                             $"(first at vertex {firstBadSumVertex}) — these will deform incorrectly");
            }
        }

        private static void CheckBlendShapes(Mesh mesh, List<string> problems)
        {
            int count = mesh.blendShapeCount;
            if (count == 0) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                string name = mesh.GetBlendShapeName(i);

                if (string.IsNullOrEmpty(name))
                {
                    problems.Add($"blendshape {i} has no name");
                    continue;
                }

                // Duplicate names make a shape unaddressable by animation, which targets shapes by name.
                if (!seen.Add(name))
                    problems.Add($"blendshape name '{name}' appears more than once — animations addressing it by name are ambiguous");

                if (mesh.GetBlendShapeFrameCount(i) == 0)
                    problems.Add($"blendshape '{name}' has no frames");
            }
        }
    }
}
