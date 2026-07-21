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
    }
}
