using System;
using System.Collections.Generic;
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
                filter.sharedMesh = DecimateMesh(filter.sharedMesh, decimater);
            }
            else if (smr != null && smr.sharedMesh != null)
            {
                smr.sharedMesh = DecimateMesh(smr.sharedMesh, decimater);
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

                int targetTriangles = settings.targetTriangleCount;
                if (targetTriangles <= 0)
                {
                    targetTriangles = Mathf.RoundToInt((originalMesh.triangles.Length / 3) * settings.decimationRatio);
                }

                targetTriangles = Mathf.Max(3, targetTriangles);

                var targetOptions = new TargetConditions()
                {
                    faceCount = targetTriangles,
                    maxMetrix = settings.targetMetric
                };

                decimator.Execute(originalMesh, param, targetOptions, false);
                Mesh newMesh = decimator.ToMesh();
                newMesh.name = originalMesh.name + "_Decimated";

                if (settings.preserveBlendShapes && originalMesh.blendShapeCount > 0)
                {
                    MeshBlendShapeUtility.PreserveBlendShapes(originalMesh, newMesh);
                }

                return newMesh;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MeshDecimation] Failed to decimate mesh {originalMesh.name}: {e.Message}");
                return originalMesh;
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
            var settings = new MeshDecimater
            {
                decimationRatio = reductionRatio,
                preserveBlendShapes = true,
                preserveBoundary = true,
                preventIntersection = true
            };

            foreach (var item in meshTargets)
            {
                Mesh decimatedMesh = DecimateMesh(item.mesh, settings);
                if (decimatedMesh != null)
                {
                    if (item.renderer is SkinnedMeshRenderer smr)
                    {
                        Undo.RecordObject(smr, "Decimate Mesh");
                        smr.sharedMesh = decimatedMesh;
                    }
                    else if (item.renderer is MeshRenderer mr)
                    {
                        var mf = mr.GetComponent<MeshFilter>();
                        if (mf != null)
                        {
                            Undo.RecordObject(mf, "Decimate Mesh");
                            mf.sharedMesh = decimatedMesh;
                        }
                    }
                    finalTotalTris += decimatedMesh.triangles.Length / 3;
                }
                else
                {
                    finalTotalTris += item.triCount;
                }
            }

            Debug.Log($"[MeshDecimation] Decimated avatar from {currentTriCount} tris down to {finalTotalTris} tris.");
            return finalTotalTris;
        }
    }
}
