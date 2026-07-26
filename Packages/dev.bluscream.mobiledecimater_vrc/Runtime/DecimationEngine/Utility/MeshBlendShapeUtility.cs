using UnityEngine;

namespace UnityMeshDecimation.Utility
{
    public static class MeshBlendShapeUtility
    {
        public static void PreserveBlendShapes(Mesh source, Mesh target)
        {
            if (source == null || target == null || source.blendShapeCount == 0) return;

            Vector3[] sourceVerts = source.vertices;
            Vector3[] targetVerts = target.vertices;
            int[] mapping = new int[targetVerts.Length];

            // Build vertex mapping (closest vertex)
            for (int i = 0; i < targetVerts.Length; i++)
            {
                float minSqDist = float.MaxValue;
                int closestIdx = 0;
                for (int j = 0; j < sourceVerts.Length; j++)
                {
                    float sqDist = (targetVerts[i] - sourceVerts[j]).sqrMagnitude;
                    if (sqDist < minSqDist)
                    {
                        minSqDist = sqDist;
                        closestIdx = j;
                    }
                }
                mapping[i] = closestIdx;
            }

            for (int shapeIdx = 0; shapeIdx < source.blendShapeCount; shapeIdx++)
            {
                string shapeName = source.GetBlendShapeName(shapeIdx);
                int frameCount = source.GetBlendShapeFrameCount(shapeIdx);

                for (int frameIdx = 0; frameIdx < frameCount; frameIdx++)
                {
                    float weight = source.GetBlendShapeFrameWeight(shapeIdx, frameIdx);
                    
                    Vector3[] deltaVerts = new Vector3[source.vertexCount];
                    Vector3[] deltaNormals = new Vector3[source.vertexCount];
                    Vector3[] deltaTangents = new Vector3[source.vertexCount];
                    
                    source.GetBlendShapeFrameVertices(shapeIdx, frameIdx, deltaVerts, deltaNormals, deltaTangents);

                    Vector3[] newDeltaVerts = new Vector3[target.vertexCount];
                    Vector3[] newDeltaNormals = new Vector3[target.vertexCount];
                    Vector3[] newDeltaTangents = new Vector3[target.vertexCount];

                    for (int i = 0; i < target.vertexCount; i++)
                    {
                        int srcIdx = mapping[i];
                        newDeltaVerts[i] = deltaVerts[srcIdx];
                        newDeltaNormals[i] = deltaNormals[srcIdx];
                        newDeltaTangents[i] = deltaTangents[srcIdx];
                    }

                    target.AddBlendShapeFrame(shapeName, weight, newDeltaVerts, newDeltaNormals, newDeltaTangents);
                }
            }
        }
    }
}
