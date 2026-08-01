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
    }
}
