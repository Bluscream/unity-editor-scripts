using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Bluscream.MenuManager
{
    public static class VRCFuryMenuHelper
    {
        private static Type VFGameObjectType;
        private static Type MenuEstimatorType;
        private static Type MenuManagerType;
        private static MethodInfo EstimateMethod;
        private static MethodInfo GetRawMethod;

        private static bool initialized = false;

        private static Type FindType(Assembly assembly, string typeName)
        {
            if (assembly == null) return null;
            var type = assembly.GetType(typeName);
            if (type != null) return type;
            // Fallback for internal types if GetType fails
            return assembly.GetTypes().FirstOrDefault(t => t.FullName == typeName);
        }

        public static bool Initialize()
        {
            if (initialized) return true;

            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var vrcfEditorAssembly = assemblies.FirstOrDefault(a => a.GetName().Name == "VRCFury-Editor-Avatars");
                var vrcfRuntimeAssembly = assemblies.FirstOrDefault(a => a.GetName().Name == "VRCFury");
                var vrcfCommonAssembly = assemblies.FirstOrDefault(a => a.GetName().Name == "VRCFury-Editor-Common");

                // Fallback for some versions
                if (vrcfCommonAssembly == null) vrcfCommonAssembly = vrcfRuntimeAssembly;

                if (vrcfRuntimeAssembly == null)
                {
                    Debug.LogWarning("VRCFury assembly not found.");
                    return false;
                }

                VFGameObjectType = FindType(vrcfCommonAssembly, "VF.Utils.VFGameObject") ?? FindType(vrcfRuntimeAssembly, "VF.Utils.VFGameObject");
                MenuEstimatorType = FindType(vrcfEditorAssembly, "VF.Utils.MenuEstimator");
                MenuManagerType = FindType(vrcfEditorAssembly, "VF.Utils.MenuManager");

                if (VFGameObjectType == null) {
                    // Search all assemblies as a last resort
                    VFGameObjectType = assemblies.Select(a => FindType(a, "VF.Utils.VFGameObject")).FirstOrDefault(t => t != null);
                }

                if (VFGameObjectType == null || MenuEstimatorType == null || MenuManagerType == null)
                {
                    Debug.LogWarning($"VRCFury types not found (VFGameObject: {VFGameObjectType != null}, MenuEstimator: {MenuEstimatorType != null}, MenuManager: {MenuManagerType != null})");
                    return false;
                }

                EstimateMethod = MenuEstimatorType.GetMethod("Estimate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                GetRawMethod = MenuManagerType.GetMethod("GetRaw", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (EstimateMethod == null || GetRawMethod == null)
                {
                    Debug.LogWarning($"VRCFury methods not found (Estimate: {EstimateMethod != null}, GetRaw: {GetRawMethod != null})");
                    return false;
                }

                initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize VRCFury reflection: {ex}");
                return false;
            }
        }

        public static VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu GetMergedMenu(GameObject avatarObj)
        {
            if (!Initialize()) return null;

            try
            {
                var implicitMethod = VFGameObjectType.GetMethod("op_Implicit", new Type[] { typeof(GameObject) });
                if (implicitMethod == null) return null;

                object vfGameObject = implicitMethod.Invoke(null, new object[] { avatarObj });
                if (vfGameObject == null) return null;
                
                object menuManager = EstimateMethod.Invoke(null, new object[] { vfGameObject });
                if (menuManager == null) return null;

                return GetRawMethod.Invoke(menuManager, null) as VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to extract merged menu via VRCFury: {ex}");
            }

            return null;
        }

        public static void ApplyMovesToAvatar(GameObject avatarObject, List<MenuMoveOperation> moves)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var vrcfRuntimeAssembly = assemblies.FirstOrDefault(a => a.GetName().Name == "VRCFury");
            if (vrcfRuntimeAssembly == null) throw new Exception("VRCFury assembly not found.");

            Type vrcfuryType = FindType(vrcfRuntimeAssembly, "VF.Model.VRCFury");
            Type moveFeatureType = FindType(vrcfRuntimeAssembly, "VF.Model.Feature.MoveMenuItem");

            if (vrcfuryType == null || moveFeatureType == null) throw new Exception("VRCFury types not found.");

            // Create or find container
            Transform moveContainer = avatarObject.transform.Find("[VRCFury] Menu Moves");
            if (moveContainer != null) {
                UnityEngine.Object.DestroyImmediate(moveContainer.gameObject);
            }

            GameObject containerObj = new GameObject("[VRCFury] Menu Moves");
            containerObj.transform.SetParent(avatarObject.transform, false);

            var contentField = vrcfuryType.GetField("content", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var fromPathField = moveFeatureType.GetField("fromPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var toPathField = moveFeatureType.GetField("toPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var move in moves)
            {
                var vrcfComponent = containerObj.AddComponent(vrcfuryType);
                object feature = Activator.CreateInstance(moveFeatureType, true); // true to allow non-public constructor
                
                fromPathField.SetValue(feature, move.fromPath);
                toPathField.SetValue(feature, move.toPath);

                contentField.SetValue(vrcfComponent, feature);
            }
        }
    }
}
