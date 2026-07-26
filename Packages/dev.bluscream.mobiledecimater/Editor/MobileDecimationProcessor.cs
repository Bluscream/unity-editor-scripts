using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMeshDecimation;
using UnityMeshDecimation.Utility;

namespace Bluscream.MobileDecimater.Editor
{
    public class MobileDecimationProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Only process during actual builds
            if (report == null) return;
            
            // Check for mobile platforms
            bool isMobile = report.summary.platform == BuildTarget.Android || 
                            report.summary.platform == BuildTarget.iOS;

            if (!isMobile)
            {
                // Debug.Log($"[MobileDecimater] Skipping decimation for platform: {report.summary.platform}");
                return;
            }

            // Find all MobileDecimater components in the scene
            var decimaters = Resources.FindObjectsOfTypeAll<MobileDecimater>();
            if (decimaters.Length == 0) return;

            Debug.Log($"[MobileDecimater] Found {decimaters.Length} decimaters in scene: {scene.name}. Processing for mobile build ({report.summary.platform})...");

            foreach (var decimater in decimaters)
            {
                // Skip if it's a prefab asset
                if (EditorUtility.IsPersistent(decimater)) continue;
                if (decimater.processed) continue;
                
                ProcessGameObject(decimater);
                decimater.processed = true;
                
                // Remove the component from the build
                Object.DestroyImmediate(decimater);
            }
        }

        private void ProcessGameObject(MobileDecimater decimater)
        {
            var go = decimater.gameObject;
            var filter = go.GetComponent<MeshFilter>();
            var smr = go.GetComponent<SkinnedMeshRenderer>();

            if (filter != null && filter.sharedMesh != null)
            {
                filter.sharedMesh = Decimate(filter.sharedMesh, decimater);
            }
            else if (smr != null && smr.sharedMesh != null)
            {
                smr.sharedMesh = Decimate(smr.sharedMesh, decimater);
            }
            else
            {
                Debug.LogWarning($"[MobileDecimater] No mesh found on {go.name} to decimate.", go);
            }
        }

        private Mesh Decimate(Mesh originalMesh, MobileDecimater settings)
        {
            try
            {
                var decimator = new UnityMeshDecimation.UnityMeshDecimation();
                
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

                Debug.Log($"[MobileDecimater] Decimating {originalMesh.name} ({originalMesh.triangles.Length/3} tris) -> Target: {targetTriangles} tris.");

                decimator.Execute(originalMesh, param, targetOptions, false);
                
                Mesh newMesh = decimator.ToMesh();
                newMesh.name = originalMesh.name + "_MobileDecimated";

                // Preserve Blendshapes if requested
                if (settings.preserveBlendShapes && originalMesh.blendShapeCount > 0)
                {
                    MeshBlendShapeUtility.PreserveBlendShapes(originalMesh, newMesh);
                }
                
                Debug.Log($"[MobileDecimater] Successfully decimated {originalMesh.name}. Final tris: {newMesh.triangles.Length/3}");
                
                return newMesh;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MobileDecimater] Failed to decimate mesh {originalMesh.name}: {e.Message}");
                return originalMesh;
            }
        }

        /// <summary>
        /// Public static API to decimate avatar renderers to hit a target overall triangle count budget
        /// </summary>
        public static int DecimateAvatarMeshesToTargetTris(GameObject avatarRoot, int targetTriangles, System.Action<string> progressCallback = null)
        {
            if (avatarRoot == null || targetTriangles <= 0) return 0;

            var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            int currentTriCount = 0;
            var meshTargets = new System.Collections.Generic.List<(Renderer renderer, Mesh mesh, int triCount)>();

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
            var settings = new MobileDecimater
            {
                decimationRatio = reductionRatio,
                preserveBlendShapes = true,
                preserveBoundary = true,
                preventIntersection = true
            };

            var processor = new MobileDecimationProcessor();

            foreach (var item in meshTargets)
            {
                Mesh decimatedMesh = processor.Decimate(item.mesh, settings);
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

            Debug.Log($"[MobileDecimater] Decimated avatar from {currentTriCount} tris down to {finalTotalTris} tris.");
            return finalTotalTris;
        }
    }
}
