using UnityEngine;

namespace Bluscream.MobileDecimater
{
    [AddComponentMenu("Bluscream/Mobile Decimater")]
    public class MobileDecimater : MonoBehaviour
    {
        [Header("Decimation Settings")]
        [Range(0.01f, 1.0f)]
        [Tooltip("The ratio of triangles to keep (e.g. 0.5 means 50% of the original triangles).")]
        public float decimationRatio = 0.5f;

        [Tooltip("Optional: Specific target triangle count. If > 0, this will be used instead of ratio.")]
        public int targetTriangleCount = 0;

        [Tooltip("If enabled, will attempt to preserve blendshapes by mapping them to the decimated mesh. Note: This can be slow for meshes with many blendshapes.")]
        public bool preserveBlendShapes = true;
        
        [Header("Library Settings")]
        [Tooltip("Target error metric for decimation.")]
        public float targetMetric = 1e-4f;
        
        [Tooltip("Prevent intersection between faces (slower).")]
        public bool preventIntersection = false;
        
        [Tooltip("Preserve boundary vertices.")]
        public bool preserveBoundary = false;

        [HideInInspector]
        public bool processed = false;

        private void Reset()
        {
            // Try to find if we are on a mesh object
            var filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                // No specific action needed, but could calculate target triangles here if desired
            }
        }
    }
}
