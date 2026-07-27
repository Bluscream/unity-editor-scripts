using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRCFury
{
    /// <summary>
    /// Utility helper for creating, querying, and managing VRCFury components and features programmatically.
    /// </summary>
    public static class VRCFuryFeatureHelper
    {
        public static void ApplyMenuMoves(GameObject avatarObject, List<MenuMoveOperation> moves, string containerName = "[VRCFury] Menu Moves")
        {
            if (avatarObject == null || moves == null || moves.Count == 0) return;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var vrcfRuntimeAssembly = assemblies.FirstOrDefault(a => a.GetName().Name == "VRCFury");
            if (vrcfRuntimeAssembly == null) throw new Exception("[VRCFuryFeatureHelper] VRCFury assembly not found.");

            Type vrcfuryType = VRCFuryHelper.FindType(vrcfRuntimeAssembly, "VF.Model.VRCFury");
            Type moveFeatureType = VRCFuryHelper.FindType(vrcfRuntimeAssembly, "VF.Model.Feature.MoveMenuItem");

            if (vrcfuryType == null || moveFeatureType == null) throw new Exception("[VRCFuryFeatureHelper] VRCFury feature types not found.");

            Transform moveContainer = avatarObject.transform.Find(containerName);
            if (moveContainer != null)
            {
                UnityEngine.Object.DestroyImmediate(moveContainer.gameObject);
            }

            GameObject containerObj = new GameObject(containerName);
            containerObj.transform.SetParent(avatarObject.transform, false);

            var contentField = vrcfuryType.GetField("content", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var fromPathField = moveFeatureType.GetField("fromPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var toPathField = moveFeatureType.GetField("toPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var move in moves)
            {
                var vrcfComponent = containerObj.AddComponent(vrcfuryType);
                object feature = Activator.CreateInstance(moveFeatureType, true);

                fromPathField.SetValue(feature, move.FromPath);
                toPathField.SetValue(feature, move.ToPath);

                contentField.SetValue(vrcfComponent, feature);
            }

            Undo.RegisterCreatedObjectUndo(containerObj, "Apply VRCFury Menu Moves");
        }

        public static void ClearMenuMoves(GameObject avatarObject, string containerName = "[VRCFury] Menu Moves")
        {
            if (avatarObject == null) return;
            Transform moveContainer = avatarObject.transform.Find(containerName);
            if (moveContainer != null)
            {
                Undo.DestroyObjectImmediate(moveContainer.gameObject);
            }
        }
    }
}
