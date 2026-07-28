using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRC
{
    /// <summary>
    /// VRChat SDK utility functions (requires VRChat SDK).
    /// </summary>
    public static class Utils
    {
        /// <summary>
        /// Checks if any VRChat SDK (SDK3A, SDK3W, Base) is present in the project assemblies.
        /// </summary>
        public static bool IsVRCSDKAvailable()
        {
            return AppDomain.CurrentDomain.GetAssemblies().Any(a =>
                a.GetName().Name.StartsWith("VRC.SDK3") ||
                a.GetName().Name.StartsWith("VRC.SDKBase"));
        }

        /// <summary>
        /// Opens the VRChat SDK Control Panel window.
        /// </summary>
        public static bool OpenVRCControlPanel()
        {
            if (TryGetControlPanelType(out Type winType))
            {
                EditorWindow.GetWindow(winType, false, "VRChat SDK");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Safely closes the VRChat SDK Control Panel after running cleanup.
        /// </summary>
        public static bool SafeCloseVRCControlPanel()
        {
            if (!TryGetControlPanelType(out Type winType)) return false;
            var window = EditorWindow.GetWindow(winType, false, "VRChat SDK", false);
            if (window != null)
            {
                VRCSDKControlPanelFix.EnsureSDKControlPanelHasSelectedBuilder(window, winType);
                window.Close();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves the VRCSdkControlPanel Type across SDK packages.
        /// </summary>
        public static bool TryGetControlPanelType(out Type controlPanelType)
        {
            controlPanelType = Type.GetType("VRCSdkControlPanel, VRCSDK3A-Editor")
                ?? Type.GetType("VRCSdkControlPanel, VRC.SDKBase.Editor")
                ?? Type.GetType("VRCSdkControlPanel");
            return controlPanelType != null;
        }

        /// <summary>
        /// Gets the currently selected avatar in the active scene or selection.
        /// </summary>
        public static bool TryGetSelectedAvatarInEditor(out GameObject avatarRoot)
        {
            avatarRoot = null;
            if (Selection.activeGameObject != null)
            {
                var desc = Selection.activeGameObject.GetAvatarDescriptor();
                if (desc != null)
                {
                    avatarRoot = desc.gameObject;
                    return true;
                }
            }
            var allDescs = UnityEngine.Object.FindObjectsOfType<global::VRC.SDKBase.VRC_AvatarDescriptor>();
            if (allDescs.Length > 0 && allDescs[0] != null)
            {
                avatarRoot = allDescs[0].gameObject;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if active build target platform is Mobile (Android/Quest or iOS).
        /// </summary>
        public static bool IsMobilePlatformActive()
        {
            return Bluscream.Utils.IsMobilePlatformActive();
        }

        /// <summary>
        /// Switches active build target group.
        /// </summary>
        public static bool SwitchBuildTarget(BuildTarget targetGroup)
        {
            BuildTargetGroup group = BuildTargetGroup.Standalone;
            if (targetGroup == BuildTarget.Android) group = BuildTargetGroup.Android;
            else if (targetGroup == BuildTarget.iOS) group = BuildTargetGroup.iOS;

            return EditorUserBuildSettings.SwitchActiveBuildTarget(group, targetGroup);
        }
    }
}
