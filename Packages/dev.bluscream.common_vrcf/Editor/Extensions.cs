using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Bluscream;

namespace Bluscream.VRCFury
{
    /// <summary>
    /// Extension methods for VRCFury components and operations on avatar objects.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Attempts to get all VRCFury components on an avatar object.
        /// </summary>
        public static bool TryGetVRCFuryComponents(this GameObject avatarRoot, out List<Component> components)
        {
            components = new List<Component>();
            if (avatarRoot == null || !Utils.TryInitialize() || Utils.VRCFuryComponentType == null) return false;
            components = avatarRoot.GetComponentsInChildren(Utils.VRCFuryComponentType, true).ToList();
            return components.Count > 0;
        }

        /// <summary>
        /// Attempts to estimate VRCFury menu parameter cost for an avatar object.
        /// </summary>
        public static bool TryEstimateMenuParameterCost(this GameObject avatarRoot, out object costResult)
        {
            costResult = null;
            if (avatarRoot == null || !Utils.TryInitialize() || Utils.EstimateMethod == null || Utils.VFGameObjectType == null) return false;
            try
            {
                object vfObj = Activator.CreateInstance(Utils.VFGameObjectType, avatarRoot);
                return Utils.EstimateMethod.TryInvoke(null, out costResult, vfObj) && costResult != null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VRCFuryExtensions] Failed to estimate VRCFury menu cost: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Attempts to retrieve the raw VRCFury expression menu object for an avatar.
        /// </summary>
        public static bool TryGetRawVRCFuryMenu(this GameObject avatarRoot, out object rawMenu)
        {
            rawMenu = null;
            if (avatarRoot == null || !Utils.TryInitialize() || Utils.MenuManagerType == null || Utils.GetRawMethod == null || Utils.VFGameObjectType == null) return false;
            try
            {
                object vfObj = Activator.CreateInstance(Utils.VFGameObjectType, avatarRoot);
                object menuManagerInstance = Activator.CreateInstance(Utils.MenuManagerType, vfObj);
                return Utils.GetRawMethod.TryInvoke(menuManagerInstance, out rawMenu) && rawMenu != null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VRCFuryExtensions] Failed to get raw VRCFury menu: {ex.Message}");
                return false;
            }
        }
    }
}
