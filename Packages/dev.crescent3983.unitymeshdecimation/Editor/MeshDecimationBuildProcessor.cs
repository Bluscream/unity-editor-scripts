using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMeshDecimation.Utility;

namespace UnityMeshDecimation.Editor
{
    public class MeshDecimationProcessor : IProcessSceneWithReport
    {
        private const string AUTO_DECIMATE_PREF_KEY = "UnityMeshDecimation_AutoDecimateOnMobileBuild";

        public int callbackOrder => 0;

        public static bool AutoDecimateEnabled
        {
            get => EditorPrefs.GetBool(AUTO_DECIMATE_PREF_KEY, true);
            set => EditorPrefs.SetBool(AUTO_DECIMATE_PREF_KEY, value);
        }

        [MenuItem("Tools/Mesh Decimation/Auto-Decimate on Mobile Build", false, 100)]
        private static void ToggleAutoDecimate()
        {
            AutoDecimateEnabled = !AutoDecimateEnabled;
            Menu.SetChecked("Tools/Mesh Decimation/Auto-Decimate on Mobile Build", AutoDecimateEnabled);
            Debug.Log($"[MeshDecimation] Auto-Decimate on Mobile Build set to: {AutoDecimateEnabled}");
        }

        [MenuItem("Tools/Mesh Decimation/Auto-Decimate on Mobile Build", true)]
        private static bool ToggleAutoDecimateValidate()
        {
            Menu.SetChecked("Tools/Mesh Decimation/Auto-Decimate on Mobile Build", AutoDecimateEnabled);
            return true;
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || !AutoDecimateEnabled) return;

            bool isMobile = report.summary.platform == BuildTarget.Android ||
                            report.summary.platform == BuildTarget.iOS;

            if (!isMobile) return;

            var decimaters = UnityEngine.Object.FindObjectsOfType<MeshDecimater>();
            if (decimaters.Length == 0) return;

            Debug.Log($"[MeshDecimation] Found {decimaters.Length} decimaters in scene: {scene.name}. Processing for mobile build ({report.summary.platform})...");

            foreach (var decimater in decimaters)
            {
                if (EditorUtility.IsPersistent(decimater) || decimater.processed) continue;

                ProcessGameObject(decimater);
                decimater.processed = true;
                UnityEngine.Object.DestroyImmediate(decimater);
            }
        }

        private void ProcessGameObject(MeshDecimater decimater)
        {
            var go = decimater.gameObject;
            var filter = go.GetComponent<MeshFilter>();
            var smr = go.GetComponent<SkinnedMeshRenderer>();

            if (filter != null && filter.sharedMesh != null)
            {
                Mesh result = DecimateMesh(filter.sharedMesh, decimater);
                if (result != null) filter.sharedMesh = result;
            }
            else if (smr != null && smr.sharedMesh != null)
            {
                Mesh result = DecimateMesh(smr.sharedMesh, decimater);
                if (result != null) smr.sharedMesh = result;
            }
        }

        public static Mesh DecimateMesh(Mesh originalMesh, MeshDecimater settings)
        {
            try
            {
                var decimator = new UnityMeshDecimation();
                var param = new EdgeCollapseParameter();
                param.SetDefaultParams();
                param.PreventIntersection = settings.preventIntersection;
                param.PreserveBoundary = settings.preserveBoundary;

                int sourceTriangles = originalMesh.triangles.Length / 3;

                int targetTriangles = settings.targetTriangleCount;
                if (targetTriangles <= 0)
                {
                    targetTriangles = Mathf.RoundToInt(sourceTriangles * settings.decimationRatio);
                }

                targetTriangles = Mathf.Max(3, targetTriangles);

                // Asking the decimator to produce MORE triangles than the mesh has is meaningless and
                // throws on degenerate inputs — a 2-triangle Quad against the floor of 3 was the source of
                // the "Object reference not set" failures.
                if (sourceTriangles <= targetTriangles)
                {
                    Debug.Log($"[MeshDecimation] Skipping '{originalMesh.name}': {sourceTriangles} tri(s) is already at or below the {targetTriangles} tri target.");
                    return null;
                }

                var targetOptions = new TargetConditions()
                {
                    faceCount = targetTriangles,
                    maxMetrix = settings.targetMetric
                };

                decimator.Execute(originalMesh, param, targetOptions, false);
                Mesh newMesh = decimator.ToMesh();
                if (newMesh == null)
                {
                    Debug.LogWarning($"[MeshDecimation] Decimator produced no mesh for '{originalMesh.name}' — keeping the original.");
                    return null;
                }
                newMesh.name = originalMesh.name + "_Decimated";

                if (settings.preserveBlendShapes && originalMesh.blendShapeCount > 0)
                {
                    try
                    {
                        MeshBlendShapeUtility.PreserveBlendShapes(originalMesh, newMesh);
                    }
                    catch (Exception shapeEx)
                    {
                        // Losing the decimation because blendshape transfer failed would be worse than
                        // losing the shapes, but the caller must know the shapes are gone.
                        Debug.LogError($"[MeshDecimation] '{originalMesh.name}': decimated to {newMesh.triangles.Length / 3} tris but blendshape preservation failed ({shapeEx.Message}). {originalMesh.blendShapeCount} blendshape(s) were LOST.");
                    }
                }

                int resultTriangles = newMesh.triangles.Length / 3;
                Debug.Log($"[MeshDecimation] '{originalMesh.name}': {sourceTriangles} -> {resultTriangles} tris " +
                          $"(asked for {targetTriangles}, achieved {(sourceTriangles > 0 ? 100f * resultTriangles / sourceTriangles : 0f):F1}% of original)" +
                          $"{(resultTriangles > targetTriangles * 1.1f ? " — DECIMATOR FELL SHORT of the requested target" : "")}");

                return newMesh;
            }
            catch (Exception e)
            {
                // Returning the original here would make the caller count a failed decimation as a
                // success, which is what previously hid these failures from the triangle accounting.
                Debug.LogError($"[MeshDecimation] Failed to decimate mesh {originalMesh.name}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Public static API to decimate avatar renderers to hit a target overall triangle count budget.
        /// </summary>
        public static int DecimateAvatarMeshesToTargetTris(GameObject avatarRoot, int targetTriangles, Action<string> progressCallback = null)
        {
            if (avatarRoot == null || targetTriangles <= 0) return 0;

            var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            int currentTriCount = 0;
            var meshTargets = new List<(Renderer renderer, Mesh mesh, int triCount)>();

            foreach (var r in renderers)
            {
                Mesh m = null;
                if (r is SkinnedMeshRenderer smr) m = smr.sharedMesh;
                else if (r is MeshRenderer mr && r.GetComponent<MeshFilter>() != null) m = r.GetComponent<MeshFilter>().sharedMesh;

                if (m != null && m.triangles != null)
                {
                    int tris = m.triangles.Length / 3;
                    currentTriCount += tris;
                    meshTargets.Add((r, m, tris));
                }
            }

            if (currentTriCount <= targetTriangles)
            {
                progressCallback?.Invoke($"Mesh poly count ({currentTriCount} tris) is already within target ({targetTriangles} tris).");
                return currentTriCount;
            }

            float reductionRatio = (float)targetTriangles / currentTriCount;
            progressCallback?.Invoke($"Decimating avatar meshes from {currentTriCount} to ~{targetTriangles} tris (Ratio: {reductionRatio:P1})...");

            int finalTotalTris = 0;
            var untouched = new List<string>();
            var pass1Settings = new MeshDecimater
            {
                decimationRatio = reductionRatio,
                preserveBlendShapes = true,
                preserveBoundary = true,
                preventIntersection = true
            };

            var pass1Results = new List<(Renderer renderer, Mesh originalMesh, Mesh decimatedMesh, int triCount, bool isHeadOrFace)>();

            Debug.Log($"[MeshDecimation] Pass 1: {meshTargets.Count} mesh(es), {currentTriCount:N0} -> {targetTriangles:N0} tris (ratio {reductionRatio:P1}), preserving boundaries and blendshapes.");

            foreach (var item in meshTargets)
            {
                bool isHeadOrFace = item.renderer != null && (
                    item.renderer.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.renderer.name.IndexOf("face", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.renderer.name.IndexOf("eye", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.renderer.name.IndexOf("mouth", StringComparison.OrdinalIgnoreCase) >= 0
                );

                Mesh decimatedMesh = DecimateMesh(item.mesh, pass1Settings);
                if (decimatedMesh == null) untouched.Add(item.renderer != null ? item.renderer.name : item.mesh.name);

                int count = decimatedMesh != null ? decimatedMesh.triangles.Length / 3 : item.triCount;
                pass1Results.Add((item.renderer, item.mesh, decimatedMesh ?? item.mesh, count, isHeadOrFace));
                finalTotalTris += count;
            }

            // Pass 2: If Pass 1 missed the triangle budget target, re-decimate non-facial body & clothing meshes
            // with relaxed boundary / intersection constraints to reach the target.
            if (finalTotalTris > targetTriangles)
            {
                int currentOverhead = finalTotalTris - targetTriangles;
                int nonFacialTris = pass1Results.Where(p => !p.isHeadOrFace).Sum(p => p.triCount);

                if (nonFacialTris > 0)
                {
                    float pass2Ratio = Mathf.Max(0.05f, (float)(nonFacialTris - currentOverhead) / nonFacialTris);
                    progressCallback?.Invoke($"Pass 2: Relaxing constraints on non-facial meshes to meet triangle target ({finalTotalTris} -> ~{targetTriangles} tris)...");
                    Debug.Log($"[MeshDecimation] Pass 2: still {finalTotalTris:N0} > {targetTriangles:N0}. Re-decimating {pass1Results.Count(p => !p.isHeadOrFace)} non-facial mesh(es) at ratio {pass2Ratio:P1} with boundaries and intersection checks relaxed.");

                    var pass2Settings = new MeshDecimater
                    {
                        decimationRatio = pass2Ratio,
                        preserveBlendShapes = true,
                        preserveBoundary = false,
                        preventIntersection = false
                    };

                    finalTotalTris = 0;
                    for (int i = 0; i < pass1Results.Count; i++)
                    {
                        var item = pass1Results[i];
                        if (!item.isHeadOrFace && item.originalMesh != null)
                        {
                            Mesh relaxedMesh = DecimateMesh(item.originalMesh, pass2Settings);
                            if (relaxedMesh != null)
                            {
                                int c = relaxedMesh.triangles.Length / 3;
                                pass1Results[i] = (item.renderer, item.originalMesh, relaxedMesh, c, item.isHeadOrFace);
                                finalTotalTris += c;
                                continue;
                            }
                        }
                        finalTotalTris += item.triCount;
                    }
                }
            }

            // Pass 3: If targetTriangles budget is STILL exceeded, perform targeted decimation on non-facial meshes to strictly enforce rank budget.
            if (finalTotalTris > targetTriangles)
            {
                int currentOverhead = finalTotalTris - targetTriangles;
                int nonFacialTris = pass1Results.Where(p => !p.isHeadOrFace).Sum(p => p.triCount);

                if (nonFacialTris > 0)
                {
                    float pass3Ratio = Mathf.Max(0.01f, (float)(nonFacialTris - currentOverhead) / nonFacialTris);
                    progressCallback?.Invoke($"Pass 3: Aggressive decimation on clothing/props to enforce triangle rank budget ({finalTotalTris} -> {targetTriangles} tris)...");
                    Debug.Log($"[MeshDecimation] Pass 3: still {finalTotalTris:N0} > {targetTriangles:N0}. Aggressive pass at ratio {pass3Ratio:P1}, blendshape preservation DISABLED on non-facial meshes.");

                    var pass3Settings = new MeshDecimater
                    {
                        decimationRatio = pass3Ratio,
                        preserveBlendShapes = false,
                        preserveBoundary = false,
                        preventIntersection = false
                    };

                    finalTotalTris = 0;
                    for (int i = 0; i < pass1Results.Count; i++)
                    {
                        var item = pass1Results[i];
                        if (!item.isHeadOrFace && item.originalMesh != null)
                        {
                            Mesh aggressiveMesh = DecimateMesh(item.originalMesh, pass3Settings);
                            if (aggressiveMesh != null)
                            {
                                int c = aggressiveMesh.triangles.Length / 3;
                                pass1Results[i] = (item.renderer, item.originalMesh, aggressiveMesh, c, item.isHeadOrFace);
                                finalTotalTris += c;
                                continue;
                            }
                        }
                        finalTotalTris += item.triCount;
                    }
                }
            }

            // Apply final decimated meshes to renderers
            foreach (var item in pass1Results)
            {
                if (item.renderer is SkinnedMeshRenderer smr)
                {
                    Undo.RecordObject(smr, "Decimate Mesh");
                    smr.sharedMesh = item.decimatedMesh;
                }
                else if (item.renderer is MeshRenderer mr)
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf != null)
                    {
                        Undo.RecordObject(mf, "Decimate Mesh");
                        mf.sharedMesh = item.decimatedMesh;
                    }
                }
            }

            if (finalTotalTris > targetTriangles)
            {
                Debug.LogWarning($"[MeshDecimation] TARGET MISSED: {currentTriCount:N0} -> {finalTotalTris:N0} tris, target was {targetTriangles:N0} " +
                                 $"({finalTotalTris - targetTriangles:N0} over, {100f * finalTotalTris / currentTriCount:F1}% of original retained). " +
                                 $"All three passes ran. The decimator could not reach the requested ratio on these meshes — " +
                                 $"{(untouched.Count > 0 ? $"{untouched.Count} mesh(es) could not be decimated at all: {string.Join(", ", untouched.Take(10))}{(untouched.Count > 10 ? $" (+{untouched.Count - 10} more)" : "")}. " : "")}" +
                                 $"The avatar will not reach its triangle rank target.");
            }
            else
            {
                Debug.Log($"[MeshDecimation] Decimated avatar from {currentTriCount:N0} tris down to {finalTotalTris:N0} tris (target {targetTriangles:N0}) — within budget.");
            }

            if (untouched.Count > 0)
                Debug.LogWarning($"[MeshDecimation] {untouched.Count} mesh(es) were left at their original size: {string.Join(", ", untouched)}");

            return finalTotalTris;
        }
    }
}
