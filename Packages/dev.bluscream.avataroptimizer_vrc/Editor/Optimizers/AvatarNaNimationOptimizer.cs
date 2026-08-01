using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Implements NaNimation toggles inspired by d4rkAvatarOptimizer:
    /// Enables merging separately toggleable meshes into a single SkinnedMeshRenderer
    /// by assigning submesh vertices a NaN-toggle bone whose scale is animated to NaN when toggled off.
    /// </summary>
    public static class AvatarNaNimationOptimizer
    {
        public const string NaNToggleBonePrefix = "NaN_Toggle_";

        public static Transform GetOrCreateNaNToggleBone(GameObject avatarRoot, string toggleName)
        {
            if (avatarRoot == null || string.IsNullOrEmpty(toggleName)) return null;

            string boneName = $"{NaNToggleBonePrefix}{toggleName}";
            Transform existing = avatarRoot.transform.Find(boneName);
            if (existing != null) return existing;

            GameObject go = new GameObject(boneName);
            Undo.RegisterCreatedObjectUndo(go, "Create NaN Toggle Bone");
            go.transform.SetParent(avatarRoot.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            return go.transform;
        }

        public static void InjectNaNAnimationCurves(AnimationClip clip, string relativeBonePath, bool isVisible)
        {
            if (clip == null || string.IsNullOrEmpty(relativeBonePath)) return;

            Undo.RecordObject(clip, "Inject NaN Animation Curves");

            float scaleVal = isVisible ? 1.0f : float.NaN;

            AnimationCurve curveX = AnimationCurve.Constant(0f, 1f / clip.frameRate, scaleVal);
            AnimationCurve curveY = AnimationCurve.Constant(0f, 1f / clip.frameRate, scaleVal);
            AnimationCurve curveZ = AnimationCurve.Constant(0f, 1f / clip.frameRate, scaleVal);

            clip.SetCurve(relativeBonePath, typeof(Transform), "m_LocalScale.x", curveX);
            clip.SetCurve(relativeBonePath, typeof(Transform), "m_LocalScale.y", curveY);
            clip.SetCurve(relativeBonePath, typeof(Transform), "m_LocalScale.z", curveZ);

            EditorUtility.SetDirty(clip);
        }

        /// <summary>
        /// Renderers whose GameObject active state is driven by an animation curve, mapped to the
        /// hierarchy path that drives them. These are the meshes that cannot normally be merged: merging
        /// would destroy the GameObject the toggle animates.
        /// </summary>
        public static Dictionary<Renderer, string> CollectToggledRenderers(GameObject avatarRoot)
        {
            var result = new Dictionary<Renderer, string>();
            if (avatarRoot == null) return result;

            var togglePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (Animator anim in avatarRoot.GetComponentsInChildren<Animator>(true))
            {
                if (anim == null || anim.runtimeAnimatorController == null) continue;
                AnimationClip[] clips = anim.runtimeAnimatorController.animationClips;
                if (clips == null) continue;

                foreach (AnimationClip clip in clips)
                {
                    if (clip == null) continue;
                    foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (b.type == typeof(GameObject) && b.propertyName == "m_IsActive")
                            togglePaths.Add(b.path);
                    }
                }
            }

            if (togglePaths.Count == 0) return result;

            foreach (Renderer r in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                string path = AnimationUtility.CalculateTransformPath(r.transform, avatarRoot.transform);
                if (togglePaths.Contains(path)) result[r] = path;
            }

            return result;
        }

        /// <summary>
        /// True when every vertex of the mesh leaves its fourth bone slot unused, so a zero-weight toggle
        /// bone can be added without displacing real skinning influence.
        /// </summary>
        /// <remarks>
        /// The toggle works because the fourth influence is added at weight 0. Skinning multiplies each
        /// bone matrix by its weight, and 0 * NaN is NaN, so scaling the toggle bone to NaN drives every
        /// vertex of that mesh to NaN and the GPU discards the triangles — while a scale of 1 leaves the
        /// result untouched, since the term contributes nothing. Vertices already using four bones would
        /// have to give one up, which changes deformation, so those meshes are skipped instead.
        /// </remarks>
        public static bool CanTakeToggleBone(Mesh mesh)
        {
            if (mesh == null) return false;

            BoneWeight[] weights = mesh.boneWeights;
            if (weights == null || weights.Length == 0) return false;

            foreach (BoneWeight bw in weights)
                if (bw.weight3 != 0f) return false;

            return true;
        }

        /// <summary>
        /// Rewrites the GameObject active-state curves for <paramref name="togglePath"/> into NaN scale
        /// curves on <paramref name="toggleBonePath"/>, so the toggle survives the merge.
        /// </summary>
        /// <returns>Number of clips rewritten.</returns>
        public static int RewriteToggleCurves(GameObject avatarRoot, string togglePath, string toggleBonePath)
        {
            if (avatarRoot == null || string.IsNullOrEmpty(togglePath)) return 0;

            int rewritten = 0;

            foreach (Animator anim in avatarRoot.GetComponentsInChildren<Animator>(true))
            {
                if (anim == null || anim.runtimeAnimatorController == null) continue;
                AnimationClip[] clips = anim.runtimeAnimatorController.animationClips;
                if (clips == null) continue;

                foreach (AnimationClip clip in clips)
                {
                    if (clip == null) continue;

                    var binding = new EditorCurveBinding { path = togglePath, type = typeof(GameObject), propertyName = "m_IsActive" };
                    AnimationCurve activeCurve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (activeCurve == null) continue;

                    // Never edit an asset the pipeline did not generate — Step 4 clones the clips it
                    // rewrites, and anything still pointing at a project asset must be left alone.
                    string clipPath = AssetDatabase.GetAssetPath(clip);
                    if (!string.IsNullOrEmpty(clipPath) && !clipPath.Contains("_AVATAROPTIMIZER"))
                    {
                        Debug.LogWarning($"[AvatarNaNimationOptimizer] Skipping NaNimation rewrite of '{clip.name}': it is still the original project asset at '{clipPath}'. Enable animation remapping so the clip is cloned first.");
                        continue;
                    }

                    // active -> scale 1, inactive -> scale NaN, preserving the original key times.
                    var scaleCurve = new AnimationCurve();
                    foreach (Keyframe key in activeCurve.keys)
                        scaleCurve.AddKey(new Keyframe(key.time, key.value > 0.5f ? 1f : float.NaN) { inTangent = 0f, outTangent = 0f });

                    AnimationUtility.SetEditorCurve(clip, binding, null); // drop the old active curve

                    foreach (string axis in new[] { "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" })
                    {
                        AnimationUtility.SetEditorCurve(
                            clip,
                            new EditorCurveBinding { path = toggleBonePath, type = typeof(Transform), propertyName = axis },
                            scaleCurve);
                    }

                    EditorUtility.SetDirty(clip);
                    rewritten++;
                }
            }

            if (rewritten > 0)
                Debug.Log($"[AvatarNaNimationOptimizer] Rewrote {rewritten} clip(s): '{togglePath}' active-state -> NaN scale on '{toggleBonePath}'.");

            return rewritten;
        }
    }
}
