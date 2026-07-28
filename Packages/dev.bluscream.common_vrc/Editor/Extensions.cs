using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using global::VRC.SDKBase;

namespace Bluscream.VRC
{
    /// <summary>
    /// Extension methods for VRChat Avatar descriptors, GameObjects, and components.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Finds the VRC_AvatarDescriptor on a GameObject or any of its parents/children.
        /// </summary>
        public static VRC_AvatarDescriptor GetAvatarDescriptor(this GameObject go)
        {
            if (go == null) return null;
            var desc = go.GetComponent<VRC_AvatarDescriptor>();
            if (desc != null) return desc;
            desc = go.GetComponentInParent<VRC_AvatarDescriptor>();
            if (desc != null) return desc;
            return go.GetComponentInChildren<VRC_AvatarDescriptor>(true);
        }

        /// <summary>
        /// Finds the PipelineManager component on an avatar object.
        /// </summary>
        public static bool TryGetPipelineManager(this GameObject avatarRoot, out Component pipelineManager)
        {
            pipelineManager = null;
            if (avatarRoot == null) return false;
            pipelineManager = avatarRoot.GetComponent("PipelineManager")
                ?? avatarRoot.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c != null && c.GetType().Name == "PipelineManager");
            return pipelineManager != null;
        }

        /// <summary>
        /// Attempts to get the blueprint ID string from an avatar PipelineManager.
        /// </summary>
        public static bool TryGetBlueprintID(this GameObject avatarRoot, out string blueprintId)
        {
            blueprintId = null;
            if (!TryGetPipelineManager(avatarRoot, out var pm)) return false;
            try
            {
                var field = pm.GetType().GetField("blueprintId");
                if (field != null) { blueprintId = field.GetValue(pm) as string; return !string.IsNullOrEmpty(blueprintId); }
                var prop = pm.GetType().GetProperty("blueprintId");
                if (prop != null) { blueprintId = prop.GetValue(pm) as string; return !string.IsNullOrEmpty(blueprintId); }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Sets the blueprint ID on an avatar's PipelineManager.
        /// </summary>
        public static bool SetBlueprintID(this GameObject avatarRoot, string blueprintId)
        {
            if (!TryGetPipelineManager(avatarRoot, out var pm)) return false;
            try
            {
                Undo.RecordObject(pm, "Set Blueprint ID");
                var field = pm.GetType().GetField("blueprintId");
                if (field != null) { field.SetValue(pm, blueprintId); EditorUtility.SetDirty(pm); return true; }
                var prop = pm.GetType().GetProperty("blueprintId");
                if (prop != null) { prop.SetValue(pm, blueprintId); EditorUtility.SetDirty(pm); return true; }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Clears the blueprint ID on an avatar's PipelineManager.
        /// </summary>
        public static bool ClearBlueprintID(this GameObject avatarRoot)
        {
            return SetBlueprintID(avatarRoot, "");
        }

        /// <summary>
        /// Gets all PhysBone components attached to an avatar.
        /// </summary>
        public static List<Component> FindPhysBones(this GameObject avatarRoot)
        {
            if (avatarRoot == null) return new List<Component>();
            return avatarRoot.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && c.GetType().Name == "VRCPhysBone")
                .ToList();
        }

        /// <summary>
        /// Gets all PhysBoneCollider components attached to an avatar.
        /// </summary>
        public static List<Component> FindPhysBoneColliders(this GameObject avatarRoot)
        {
            if (avatarRoot == null) return new List<Component>();
            return avatarRoot.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && c.GetType().Name == "VRCPhysBoneCollider")
                .ToList();
        }

        /// <summary>
        /// Gets all ContactReceiver or ContactSender components attached to an avatar.
        /// </summary>
        public static List<Component> FindContacts(this GameObject avatarRoot)
        {
            if (avatarRoot == null) return new List<Component>();
            return avatarRoot.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && (c.GetType().Name == "VRCContactReceiver" || c.GetType().Name == "VRCContactSender"))
                .ToList();
        }

        /// <summary>
        /// Gets all constraint components attached to an avatar.
        /// </summary>
        public static List<Component> FindConstraints(this GameObject avatarRoot)
        {
            if (avatarRoot == null) return new List<Component>();
            return avatarRoot.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && c.GetType().Name.EndsWith("Constraint"))
                .ToList();
        }

        /// <summary>
        /// Calculates total material slots across all renderers on an avatar.
        /// </summary>
        public static int GetTotalMaterialSlotCount(this GameObject avatarRoot)
        {
            if (avatarRoot == null) return 0;
            return avatarRoot.GetComponentsInChildren<Renderer>(true)
                .Sum(r => r.sharedMaterials != null ? r.sharedMaterials.Length : 0);
        }

        /// <summary>
        /// Calculates total polygon (triangle) count across all MeshRenderers and SkinnedMeshRenderers on an avatar.
        /// </summary>
        public static int GetTotalPolygonCount(this GameObject avatarRoot)
        {
            if (avatarRoot == null) return 0;
            int totalPolys = 0;

            foreach (var smr in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr != null && smr.sharedMesh != null)
                    totalPolys += smr.sharedMesh.triangles.Length / 3;
            }

            foreach (var mf in avatarRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf != null && mf.sharedMesh != null)
                {
                    var renderer = mf.GetComponent<MeshRenderer>();
                    if (renderer != null && renderer.enabled)
                        totalPolys += mf.sharedMesh.triangles.Length / 3;
                }
            }

            return totalPolys;
        }
    }
}
