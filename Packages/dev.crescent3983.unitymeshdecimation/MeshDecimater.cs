using UnityEngine;

namespace UnityMeshDecimation
{
    [AddComponentMenu("Mesh Decimation/Mesh Decimater")]
    public class MeshDecimater : MonoBehaviour
    {
        [Header("Decimation Settings")]
        [Range(0.01f, 1.0f)]
        [Tooltip("The ratio of triangles to keep (e.g. 0.5 means 50% of original triangles).")]
        public float decimationRatio = 0.5f;

        [Tooltip("Optional: Specific target triangle count. If > 0, this will be used instead of ratio.")]
        public int targetTriangleCount = 0;

        [Tooltip("If enabled, will attempt to preserve blendshapes by mapping them to the decimated mesh.")]
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
    }
}
