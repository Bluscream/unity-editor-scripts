using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

namespace Bluscream.VRC
{
    /// <summary>
    /// Comprehensive helper functions for working with VRChat Avatars, components, descriptors, pipeline managers, and metrics.
    /// </summary>
    public static class VRCAvatarHelper
    {
        public static VRC_AvatarDescriptor GetAvatarDescriptor(GameObject avatarRoot)
        {
            if (avatarRoot == null) return null;
            return avatarRoot.GetComponent<VRC_AvatarDescriptor>() ?? avatarRoot.GetComponentInChildren<VRC_AvatarDescriptor>(true);
        }

        public static Component GetPipelineManager(GameObject avatarRoot)
        {
            if (avatarRoot == null) return null;
            Component pm = avatarRoot.GetComponent("PipelineManager");
            if (pm != null) return pm;
            return avatarRoot.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c != null && c.GetType().Name == "PipelineManager");
        }

        public static string GetBlueprintID(GameObject avatarRoot)
        {
            Component pm = GetPipelineManager(avatarRoot);
            if (pm == null) return null;
            try
            {
                var field = pm.GetType().GetField("blueprintId");
                if (field != null) return field.GetValue(pm) as string;
                var prop = pm.GetType().GetProperty("blueprintId");
                if (prop != null) return prop.GetValue(pm) as string;
            }
            catch { }
            return null;
        }

        public static bool SetBlueprintID(GameObject avatarRoot, string blueprintId)
        {
            Component pm = GetPipelineManager(avatarRoot);
            if (pm == null) return false;
            try
            {
                Undo.RecordObject(pm, "Set Avatar Blueprint ID");
                var field = pm.GetType().GetField("blueprintId");
                if (field != null) { field.SetValue(pm, blueprintId); EditorUtility.SetDirty(pm); return true; }
                var prop = pm.GetType().GetProperty("blueprintId");
                if (prop != null) { prop.SetValue(pm, blueprintId); EditorUtility.SetDirty(pm); return true; }
            }
            catch { }
            return false;
        }

        public static bool ClearBlueprintID(GameObject avatarRoot)
        {
            return SetBlueprintID(avatarRoot, string.Empty);
        }

        public static List<Component> FindPhysBones(GameObject avatarRoot)
        {
            if (avatarRoot == null) return new List<Component>();
            return avatarRoot.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && (c.GetType().Name == "VRCPhysBone" || c.GetType().Name == "VRCPhysBoneBase"))
                .ToList();
        }

        public static List<Component> FindPhysBoneColliders(GameObject avatarRoot)
        {
            if (avatarRoot == null) return new List<Component>();
            return avatarRoot.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && c.GetType().Name.Contains("VRCPhysBoneCollider"))
                .ToList();
        }

        public static List<Component> FindContacts(GameObject avatarRoot)
        {
            if (avatarRoot == null) return new List<Component>();
            return avatarRoot.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && (c.GetType().Name.Contains("VRCContactSender") || c.GetType().Name.Contains("VRCContactReceiver")))
                .ToList();
        }

        public static List<Component> FindConstraints(GameObject avatarRoot)
        {
            if (avatarRoot == null) return new List<Component>();
            return avatarRoot.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && c.GetType().Name.ToLowerInvariant().Contains("constraint"))
                .ToList();
        }

        public static int GetTotalMaterialSlotCount(GameObject avatarRoot)
        {
            if (avatarRoot == null) return 0;
            int count = 0;
            foreach (var r in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r != null && r.sharedMaterials != null) count += r.sharedMaterials.Length;
            }
            return count;
        }

        public static int GetTotalPolygonCount(GameObject avatarRoot)
        {
            if (avatarRoot == null) return 0;
            int tris = 0;
            foreach (var r in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    tris += smr.sharedMesh.triangles.Length / 3;
                }
                else if (r is MeshRenderer mr)
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                }
            }
            return tris;
        }

        public static BuildTarget GetActiveBuildTarget()
        {
            return EditorUserBuildSettings.activeBuildTarget;
        }

        public static bool IsMobilePlatformActive()
        {
            var target = GetActiveBuildTarget();
            return target == BuildTarget.Android || target == BuildTarget.iOS;
        }

        public static bool IsPCPlatformActive()
        {
            return GetActiveBuildTarget() == BuildTarget.StandaloneWindows || GetActiveBuildTarget() == BuildTarget.StandaloneWindows64;
        }
    }
}
