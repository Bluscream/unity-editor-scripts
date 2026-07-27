using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRC
{
    /// <summary>
    /// Common VRChat utilities and helper methods across Editor tools and packages.
    /// </summary>
    public static class VRCCommonHelper
    {
        public const string VRC_COMMON_VERSION = "1.2.0";

        public static bool IsVRCSDKAvailable()
        {
            Type t = Type.GetType("VRC.SDKBase.Editor.VRCSdkControlPanel, VRC.SDKBase.Editor")
                ?? Type.GetType("VRCSdkControlPanel");
            return t != null;
        }

        public static void OpenVRCControlPanel()
        {
            try
            {
                Type windowType = Type.GetType("VRCSdkControlPanel, VRCSDK3A-Editor")
                    ?? Type.GetType("VRCSdkControlPanel, VRC.SDKBase.Editor")
                    ?? Type.GetType("VRCSdkControlPanel");

                if (windowType != null)
                {
                    EditorWindow.GetWindow(windowType);
                }
                else
                {
                    Debug.LogWarning("[VRCCommonHelper] Could not find VRCSdkControlPanel window type.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VRCCommonHelper] Failed to open VRChat Control Panel: {ex.Message}");
            }
        }

        public static void SafeCloseVRCControlPanel()
        {
            try
            {
                Type windowType = Type.GetType("VRCSdkControlPanel, VRCSDK3A-Editor")
                    ?? Type.GetType("VRCSdkControlPanel, VRC.SDKBase.Editor")
                    ?? Type.GetType("VRCSdkControlPanel");

                if (windowType != null)
                {
                    var window = EditorWindow.GetWindow(windowType, false, null, false);
                    if (window != null)
                    {
                        window.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VRCCommonHelper] SafeCloseVRCControlPanel warning: {ex.Message}");
            }
        }

        public static GameObject GetSelectedAvatarInEditor()
        {
            if (Selection.activeGameObject != null)
            {
                var desc = VRCAvatarHelper.GetAvatarDescriptor(Selection.activeGameObject);
                if (desc != null) return desc.gameObject;
            }
            var allDescs = UnityEngine.Object.FindObjectsOfType<global::VRC.SDKBase.VRC_AvatarDescriptor>();
            return allDescs.Length > 0 ? allDescs[0].gameObject : null;
        }

        public static bool SwitchBuildTarget(BuildTarget targetGroup)
        {
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(targetGroup);
            if (EditorUserBuildSettings.activeBuildTarget == targetGroup) return true;
            return EditorUserBuildSettings.SwitchActiveBuildTarget(group, targetGroup);
        }
    }
}
