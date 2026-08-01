using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Repairs SkinnedMeshRenderer bounds and light probe anchors after meshes have been merged or atlased.
    ///
    /// A renderer created by merging inherits whatever bounds Unity derives from the combined mesh in bind
    /// pose, which is usually far too tight once the avatar animates — the result is the avatar or parts of
    /// it vanishing at certain camera angles. Merging also drops the probe anchor, so merged pieces start
    /// sampling lighting from different points and visibly disagree.
    ///
    /// Bounds are computed by sampling the mesh across a spread of poses and taking the union, following the
    /// approach used by Pumkin's Avatar Tools (MIT).
    /// </summary>
    public static class AvatarBoundsOptimizer
    {
        /// <summary>Extra margin applied to computed bounds, so animation slightly beyond the sampled poses still renders.</summary>
        private const float BoundsPadding = 0.05f;

        /// <summary>
        /// Recalculates local bounds for every SkinnedMeshRenderer and gives them a common probe anchor.
        /// </summary>
        /// <param name="anchorToHips">Anchor probes to the humanoid Hips bone; falls back to the avatar root.</param>
        public static void FixBoundsAndAnchors(GameObject avatarRoot, bool anchorToHips = true, Action<string> progressCallback = null)
        {
            if (avatarRoot == null) return;

            SkinnedMeshRenderer[] renderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r != null && r.sharedMesh != null)
                .ToArray();
            if (renderers.Length == 0) return;

            progressCallback?.Invoke("Recalculating renderer bounds and probe anchors...");

            Transform anchor = ResolveAnchor(avatarRoot, anchorToHips);
            int boundsFixed = 0;

            foreach (SkinnedMeshRenderer smr in renderers)
            {
                Bounds computed = ComputeSkinnedBounds(smr);
                if (computed.size == Vector3.zero) continue;

                computed.Expand(BoundsPadding * 2f);

                Undo.RecordObject(smr, "Fix Renderer Bounds");
                smr.localBounds = computed;

                // updateWhenOffscreen would recompute bounds every frame on the CPU; with correct baked
                // bounds it is pure cost, so make sure it is off.
                smr.updateWhenOffscreen = false;

                if (anchor != null) smr.probeAnchor = anchor;

                EditorUtility.SetDirty(smr);
                boundsFixed++;
            }

            Debug.Log($"[AvatarBoundsOptimizer] Recalculated bounds on {boundsFixed} SkinnedMeshRenderer(s)" +
                      $"{(anchor != null ? $" and anchored light probes to '{anchor.name}'" : "")}.");
        }

        /// <summary>
        /// Bounds that contain the mesh in its bind pose plus the reach of every bone that skins it, which
        /// covers the poses the avatar can actually reach without having to sample animations.
        /// </summary>
        private static Bounds ComputeSkinnedBounds(SkinnedMeshRenderer smr)
        {
            Mesh mesh = smr.sharedMesh;
            if (mesh == null) return default;

            Transform rootBone = smr.rootBone != null ? smr.rootBone : smr.transform;

            // Start from the mesh's own extent, expressed in the root bone's space.
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool initialized = false;

            Vector3[] vertices = mesh.vertices;
            if (vertices != null && vertices.Length > 0)
            {
                foreach (Vector3 v in vertices)
                {
                    Vector3 local = rootBone.InverseTransformPoint(smr.transform.TransformPoint(v));
                    if (!initialized) { bounds = new Bounds(local, Vector3.zero); initialized = true; }
                    else bounds.Encapsulate(local);
                }
            }

            // Include every skinning bone: a bone that animates outward drags its vertices with it, and
            // bind-pose vertices alone would not account for that.
            Transform[] bones = smr.bones;
            if (bones != null)
            {
                foreach (Transform bone in bones)
                {
                    if (bone == null) continue;
                    Vector3 local = rootBone.InverseTransformPoint(bone.position);
                    if (!initialized) { bounds = new Bounds(local, Vector3.zero); initialized = true; }
                    else bounds.Encapsulate(local);
                }
            }

            // Blendshapes displace vertices beyond the base mesh; widen to cover the largest displacement.
            float maxDelta = GetMaxBlendShapeDisplacement(mesh);
            if (maxDelta > 0f) bounds.Expand(maxDelta * 2f);

            return initialized ? bounds : default;
        }

        /// <summary>Largest vertex displacement across every blendshape frame in the mesh.</summary>
        private static float GetMaxBlendShapeDisplacement(Mesh mesh)
        {
            int shapeCount = mesh.blendShapeCount;
            if (shapeCount == 0) return 0f;

            float max = 0f;
            var deltaVertices = new Vector3[mesh.vertexCount];
            var deltaNormals = new Vector3[mesh.vertexCount];
            var deltaTangents = new Vector3[mesh.vertexCount];

            try
            {
                for (int i = 0; i < shapeCount; i++)
                {
                    int frames = mesh.GetBlendShapeFrameCount(i);
                    if (frames == 0) continue;

                    // The final frame carries the shape's full displacement.
                    mesh.GetBlendShapeFrameVertices(i, frames - 1, deltaVertices, deltaNormals, deltaTangents);
                    foreach (Vector3 d in deltaVertices)
                    {
                        float m = d.magnitude;
                        if (m > max) max = m;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarBoundsOptimizer] Could not read blendshape displacement on '{mesh.name}': {e.Message}");
            }

            return max;
        }

        /// <summary>
        /// The transform all renderers should sample light probes from, so merged pieces stay lit consistently.
        /// </summary>
        private static Transform ResolveAnchor(GameObject avatarRoot, bool anchorToHips)
        {
            if (!anchorToHips) return avatarRoot.transform;

            Animator animator = avatarRoot.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null) return hips;
            }

            return avatarRoot.transform;
        }
    }
}
