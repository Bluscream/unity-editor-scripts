using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCFury
{
    /// <summary>
    /// Core reflection and initialization helper for interacting with VRCFury types and components dynamically.
    /// </summary>
    public static class VRCFuryHelper
    {
        public static Type VFGameObjectType { get; private set; }
        public static Type MenuEstimatorType { get; private set; }
        public static Type MenuManagerType { get; private set; }
        public static Type VRCFuryComponentType { get; private set; }
        public static MethodInfo EstimateMethod { get; private set; }
        public static MethodInfo GetRawMethod { get; private set; }

        public static bool IsInitialized { get; private set; }

        public static bool Initialize()
        {
            if (IsInitialized) return true;

            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var vrcfEditorAssembly = assemblies.FirstOrDefault(a => a.GetName().Name == "VRCFury-Editor-Avatars");
                var vrcfRuntimeAssembly = assemblies.FirstOrDefault(a => a.GetName().Name == "VRCFury");
                var vrcfCommonAssembly = assemblies.FirstOrDefault(a => a.GetName().Name == "VRCFury-Editor-Common") ?? vrcfRuntimeAssembly;

                if (vrcfRuntimeAssembly == null)
                {
                    Debug.LogWarning("[VRCFuryHelper] VRCFury runtime assembly not found.");
                    return false;
                }

                VFGameObjectType = FindType(vrcfCommonAssembly, "VF.Utils.VFGameObject") ?? FindType(vrcfRuntimeAssembly, "VF.Utils.VFGameObject");
                MenuEstimatorType = FindType(vrcfEditorAssembly, "VF.Utils.MenuEstimator");
                MenuManagerType = FindType(vrcfEditorAssembly, "VF.Utils.MenuManager");
                VRCFuryComponentType = FindType(vrcfRuntimeAssembly, "VF.Model.VRCFury");

                if (VFGameObjectType == null)
                {
                    VFGameObjectType = assemblies.Select(a => FindType(a, "VF.Utils.VFGameObject")).FirstOrDefault(t => t != null);
                }

                if (VFGameObjectType != null && MenuEstimatorType != null)
                {
                    EstimateMethod = MenuEstimatorType.GetMethod("Estimate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                }

                if (MenuManagerType != null)
                {
                    GetRawMethod = MenuManagerType.GetMethod("GetRaw", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }

                IsInitialized = (VRCFuryComponentType != null || (EstimateMethod != null && GetRawMethod != null));
                return IsInitialized;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VRCFuryHelper] Failed to initialize VRCFury reflection: {ex}");
                return false;
            }
        }

        public static Type FindType(Assembly assembly, string typeName)
        {
            if (assembly == null) return null;
            var type = assembly.GetType(typeName);
            if (type != null) return type;
            return assembly.GetTypes().FirstOrDefault(t => t.FullName == typeName);
        }

        public static bool IsVRCFuryInstalled()
        {
            return Initialize();
        }

        public static List<Component> GetVRCFuryComponents(GameObject avatarRoot)
        {
            if (avatarRoot == null || !Initialize() || VRCFuryComponentType == null) return new List<Component>();
            return avatarRoot.GetComponentsInChildren(VRCFuryComponentType, true).ToList();
        }
    }
}
