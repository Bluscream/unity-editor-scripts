using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Bluscream;

namespace Bluscream.VRCFury
{
    /// <summary>
    /// VRCFury utility functions (requires VRCFury).
    /// </summary>
    public static class Utils
    {
        public static Assembly VRCFuryAssembly { get; private set; }
        public static Type VRCFuryComponentType { get; private set; }
        public static Type VFGameObjectType { get; private set; }
        public static Type MenuManagerType { get; private set; }
        public static MethodInfo EstimateMethod { get; private set; }
        public static MethodInfo GetRawMethod { get; private set; }

        private static bool _initialized = false;

        /// <summary>
        /// Attempts to initialize VRCFury reflection types.
        /// </summary>
        public static bool TryInitialize()
        {
            if (_initialized) return VRCFuryComponentType != null;
            _initialized = true;

            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                VRCFuryAssembly = assemblies.FirstOrDefault(a => a.GetName().Name == "VRCFury");
                if (VRCFuryAssembly == null) return false;

                ReflectionHelper.TryFindType("VF.Model.VRCFury", out var vrcfType);
                VRCFuryComponentType = vrcfType;

                ReflectionHelper.TryFindType("VF.Model.VFGameObject", out var vfGameObjType);
                VFGameObjectType = vfGameObjType;

                ReflectionHelper.TryFindType("VF.Utils.MenuEstimator", out var menuEstimatorType);
                if (menuEstimatorType == null)
                {
                    ReflectionHelper.TryFindType("VF.Menu.MenuEstimator", out menuEstimatorType);
                }

                ReflectionHelper.TryFindType("VF.Utils.MenuManager", out var menuMgrType);
                if (menuMgrType == null)
                {
                    ReflectionHelper.TryFindType("VF.Menu.MenuManager", out menuMgrType);
                }
                MenuManagerType = menuMgrType;

                if (menuEstimatorType != null && VFGameObjectType != null)
                {
                    EstimateMethod = menuEstimatorType.GetMethod("Estimate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { VFGameObjectType }, null);
                }

                if (EstimateMethod == null && MenuManagerType != null && VFGameObjectType != null)
                {
                    EstimateMethod = MenuManagerType.GetMethod("CalculateMenuParameterCost", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { VFGameObjectType }, null);
                }

                if (MenuManagerType != null)
                {
                    GetRawMethod = MenuManagerType.GetMethod("GetRaw", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }

                return VRCFuryComponentType != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if VRCFury is installed in the project.
        /// </summary>
        public static bool IsVRCFuryInstalled()
        {
            return TryInitialize();
        }

        /// <summary>
        /// Programmatically applies menu item move operations to an avatar via VRCFury.
        /// </summary>
        public static void ApplyMenuMoves(GameObject avatarObject, List<MenuMoveOperation> moves, string containerName = "[VRCFury] Menu Moves")
        {
            if (avatarObject == null || moves == null || moves.Count == 0) return;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var vrcfRuntimeAssembly = assemblies.FirstOrDefault(a => a.GetName().Name == "VRCFury");
            if (vrcfRuntimeAssembly == null) throw new Exception("[VRCFuryFeatureHelper] VRCFury assembly not found.");

            Type vrcfuryType = null;
            Type moveFeatureType = null;
            if (!ReflectionHelper.TryFindType(vrcfRuntimeAssembly, "VF.Model.VRCFury", out vrcfuryType) ||
                !ReflectionHelper.TryFindType(vrcfRuntimeAssembly, "VF.Model.Feature.MoveMenuItem", out moveFeatureType))
                throw new Exception("[VRCFuryFeatureHelper] VRCFury feature types not found.");

            Transform moveContainer = avatarObject.transform.Find(containerName);
            if (moveContainer != null)
            {
                UnityEngine.Object.DestroyImmediate(moveContainer.gameObject);
            }

            GameObject containerObj = new GameObject(containerName);
            containerObj.transform.SetParent(avatarObject.transform, false);

            Undo.RegisterCreatedObjectUndo(containerObj, "Create VRCFury Menu Moves Container");

            foreach (var move in moves)
            {
                if (string.IsNullOrEmpty(move.FromPath) || move.ToPath == null) continue;

                Component vrcfComponent = containerObj.AddComponent(vrcfuryType);
                if (vrcfComponent == null) continue;

                object moveFeature = Activator.CreateInstance(moveFeatureType);

                ReflectionHelper.TrySetFieldValue(moveFeature, "fromPath", move.FromPath);
                ReflectionHelper.TrySetFieldValue(moveFeature, "toPath", move.ToPath);

                ReflectionHelper.TryGetFieldValue(vrcfComponent, "config", out object config);
                if (config != null)
                {
                    ReflectionHelper.TryGetFieldValue(config, "features", out object featuresListObj);
                    if (featuresListObj != null)
                    {
                        MethodInfo addMethod = featuresListObj.GetType().GetMethod("Add");
                        if (addMethod != null)
                        {
                            addMethod.Invoke(featuresListObj, new object[] { moveFeature });
                        }
                    }
                }

                EditorUtility.SetDirty(vrcfComponent);
            }

            EditorUtility.SetDirty(containerObj);
        }
    }
}
