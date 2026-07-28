using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bluscream.VRC
{
    /// <summary>
    /// Automatic Editor fix to prevent VRChat SDK Control Panel NullReferenceException
    /// on DetachFromPanelEvent when closing or docking the VRCSdkControlPanel window.
    /// </summary>
    [InitializeOnLoad]
    public static class VRCSDKControlPanelFix
    {
        static VRCSDKControlPanelFix()
        {
            EditorApplication.update -= GuardControlPanelOnUpdate;
            EditorApplication.update += GuardControlPanelOnUpdate;
        }

        private static void GuardControlPanelOnUpdate()
        {
            try
            {
                Type windowType = Type.GetType("VRCSdkControlPanel, VRCSDK3A-Editor")
                    ?? Type.GetType("VRCSdkControlPanel, VRC.SDKBase.Editor")
                    ?? Type.GetType("VRCSdkControlPanel");

                if (windowType == null) return;

                UnityEngine.Object[] windows = Resources.FindObjectsOfTypeAll(windowType);
                if (windows == null || windows.Length == 0) return;

                foreach (EditorWindow win in windows.OfType<EditorWindow>())
                {
                    EnsureSDKControlPanelHasSelectedBuilder(win, windowType);
                }
            }
            catch
            {
                // Silently guard against any reflection errors
            }
        }

        /// <summary>
        /// Ensures that the specified VRCSdkControlPanel EditorWindow instance has a valid active builder assigned,
        /// guarding against NullReferenceExceptions when the window is closed or unmounted.
        /// </summary>
        public static bool EnsureSDKControlPanelHasSelectedBuilder(EditorWindow win, Type windowType = null)
        {
            if (win == null) return false;

            try
            {
                if (windowType == null) windowType = win.GetType();

                if (ReflectionHelper.TryGetFieldValue(win, "_selectedBuilder", out object selectedBuilder) && selectedBuilder == null)
                {
                    if (ReflectionHelper.TryGetFieldValue(win, "_sdkBuilders", out Array sdkBuilders) && sdkBuilders != null && sdkBuilders.Length > 0)
                    {
                        object fallbackBuilder = sdkBuilders.GetValue(0);
                        if (fallbackBuilder != null)
                        {
                            ReflectionHelper.TrySetFieldValue(win, "_selectedBuilder", fallbackBuilder);
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }
    }
}
