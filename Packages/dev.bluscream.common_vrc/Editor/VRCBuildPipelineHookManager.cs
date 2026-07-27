using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Bluscream.VRC
{
    /// <summary>
    /// Represents the result of a build pipeline hook execution.
    /// Consumers can return HookResult.Pass() or HookResult.Cancel(...) to abort execution with or without a dialog.
    /// </summary>
    public struct HookResult
    {
        public bool Success;
        public bool Abort;
        public string ErrorMessage;
        public bool ShowDialog;

        public static HookResult Pass() => new HookResult { Success = true, Abort = false };
        public static HookResult Cancel(string message = null, bool showDialog = true) => new HookResult
        {
            Success = false,
            Abort = true,
            ErrorMessage = message,
            ShowDialog = showDialog
        };
    }

    /// <summary>
    /// Central manager for VRChat SDK build and upload pipeline hooks.
    /// Allows packages to register pre/post preprocess, build requested, AssetBundle build, and upload hooks with priority,
    /// as well as manually trigger or abort any stage.
    /// </summary>
    [InitializeOnLoad]
    public class VRCBuildPipelineHookManager : IVRCSDKPreprocessAvatarCallback, IVRCSDKPostprocessAvatarCallback, IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => 0;

        public class HookRegistration<T>
        {
            public int Priority; // Lower value = higher priority (runs first)
            public string ConsumerName;
            public Func<T, HookResult> Callback;
        }

        private static readonly List<HookRegistration<GameObject>> _preprocessHooks = new List<HookRegistration<GameObject>>();
        private static readonly List<HookRegistration<GameObject>> _postprocessHooks = new List<HookRegistration<GameObject>>();
        private static readonly List<HookRegistration<VRCSDKRequestedBuildType>> _buildRequestedHooks = new List<HookRegistration<VRCSDKRequestedBuildType>>();
        private static readonly List<HookRegistration<(GameObject avatarRoot, string bundlePath)>> _preBuildHooks = new List<HookRegistration<(GameObject, string)>>();
        private static readonly List<HookRegistration<(GameObject avatarRoot, string bundlePath)>> _postBuildHooks = new List<HookRegistration<(GameObject, string)>>();
        private static readonly List<HookRegistration<(GameObject avatarRoot, string thumbnailPath)>> _preUploadHooks = new List<HookRegistration<(GameObject, string)>>();
        private static readonly List<HookRegistration<(GameObject avatarRoot, string thumbnailPath)>> _postUploadHooks = new List<HookRegistration<(GameObject, string)>>();

        // Registration methods
        public static void RegisterPreprocessHook(Func<GameObject, HookResult> callback, int priority = 0, string consumerName = null)
        {
            _preprocessHooks.Add(new HookRegistration<GameObject> { Callback = callback, Priority = priority, ConsumerName = consumerName ?? callback.Method.DeclaringType?.Name });
        }

        public static void RegisterPostprocessHook(Func<GameObject, HookResult> callback, int priority = 0, string consumerName = null)
        {
            _postprocessHooks.Add(new HookRegistration<GameObject> { Callback = callback, Priority = priority, ConsumerName = consumerName ?? callback.Method.DeclaringType?.Name });
        }

        public static void RegisterBuildRequestedHook(Func<VRCSDKRequestedBuildType, HookResult> callback, int priority = 0, string consumerName = null)
        {
            _buildRequestedHooks.Add(new HookRegistration<VRCSDKRequestedBuildType> { Callback = callback, Priority = priority, ConsumerName = consumerName ?? callback.Method.DeclaringType?.Name });
        }

        public static void RegisterPreBuildHook(Func<(GameObject avatarRoot, string bundlePath), HookResult> callback, int priority = 0, string consumerName = null)
        {
            _preBuildHooks.Add(new HookRegistration<(GameObject, string)> { Callback = callback, Priority = priority, ConsumerName = consumerName ?? callback.Method.DeclaringType?.Name });
        }

        public static void RegisterPostBuildHook(Func<(GameObject avatarRoot, string bundlePath), HookResult> callback, int priority = 0, string consumerName = null)
        {
            _postBuildHooks.Add(new HookRegistration<(GameObject, string)> { Callback = callback, Priority = priority, ConsumerName = consumerName ?? callback.Method.DeclaringType?.Name });
        }

        public static void RegisterPreUploadHook(Func<(GameObject avatarRoot, string thumbnailPath), HookResult> callback, int priority = 0, string consumerName = null)
        {
            _preUploadHooks.Add(new HookRegistration<(GameObject, string)> { Callback = callback, Priority = priority, ConsumerName = consumerName ?? callback.Method.DeclaringType?.Name });
        }

        public static void RegisterPostUploadHook(Func<(GameObject avatarRoot, string thumbnailPath), HookResult> callback, int priority = 0, string consumerName = null)
        {
            _postUploadHooks.Add(new HookRegistration<(GameObject, string)> { Callback = callback, Priority = priority, ConsumerName = consumerName ?? callback.Method.DeclaringType?.Name });
        }

        // Invocation methods
        public static HookResult InvokePreprocessAvatar(GameObject avatarRoot)
        {
            foreach (var hook in _preprocessHooks.OrderBy(h => h.Priority))
            {
                try
                {
                    HookResult res = hook.Callback(avatarRoot);
                    if (res.Abort)
                    {
                        HandleAbort(hook.ConsumerName, "PreprocessAvatar", res);
                        return res;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[VRCBuildPipelineHookManager] Error in PreprocessAvatar hook '{hook.ConsumerName}': {ex}");
                    HookResult errorRes = HookResult.Cancel($"Hook error in {hook.ConsumerName}: {ex.Message}", true);
                    HandleAbort(hook.ConsumerName, "PreprocessAvatar", errorRes);
                    return errorRes;
                }
            }
            return HookResult.Pass();
        }

        public static HookResult InvokePostprocessAvatar(GameObject avatarRoot)
        {
            foreach (var hook in _postprocessHooks.OrderBy(h => h.Priority))
            {
                try
                {
                    HookResult res = hook.Callback(avatarRoot);
                    if (res.Abort)
                    {
                        HandleAbort(hook.ConsumerName, "PostprocessAvatar", res);
                        return res;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[VRCBuildPipelineHookManager] Error in PostprocessAvatar hook '{hook.ConsumerName}': {ex}");
                    HookResult errorRes = HookResult.Cancel($"Hook error in {hook.ConsumerName}: {ex.Message}", true);
                    HandleAbort(hook.ConsumerName, "PostprocessAvatar", errorRes);
                    return errorRes;
                }
            }
            return HookResult.Pass();
        }

        public static HookResult InvokeBuildRequested(VRCSDKRequestedBuildType buildType)
        {
            foreach (var hook in _buildRequestedHooks.OrderBy(h => h.Priority))
            {
                try
                {
                    HookResult res = hook.Callback(buildType);
                    if (res.Abort)
                    {
                        HandleAbort(hook.ConsumerName, "BuildRequested", res);
                        return res;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[VRCBuildPipelineHookManager] Error in BuildRequested hook '{hook.ConsumerName}': {ex}");
                    HookResult errorRes = HookResult.Cancel($"Hook error in {hook.ConsumerName}: {ex.Message}", true);
                    HandleAbort(hook.ConsumerName, "BuildRequested", errorRes);
                    return errorRes;
                }
            }
            return HookResult.Pass();
        }

        public static HookResult InvokePreBuild(GameObject avatarRoot, string bundlePath = null)
        {
            foreach (var hook in _preBuildHooks.OrderBy(h => h.Priority))
            {
                try
                {
                    HookResult res = hook.Callback((avatarRoot, bundlePath));
                    if (res.Abort)
                    {
                        HandleAbort(hook.ConsumerName, "PreBuild", res);
                        return res;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[VRCBuildPipelineHookManager] Error in PreBuild hook '{hook.ConsumerName}': {ex}");
                    HookResult errorRes = HookResult.Cancel($"Hook error in {hook.ConsumerName}: {ex.Message}", true);
                    HandleAbort(hook.ConsumerName, "PreBuild", errorRes);
                    return errorRes;
                }
            }
            return HookResult.Pass();
        }

        public static HookResult InvokePostBuild(GameObject avatarRoot, string bundlePath)
        {
            foreach (var hook in _postBuildHooks.OrderBy(h => h.Priority))
            {
                try
                {
                    HookResult res = hook.Callback((avatarRoot, bundlePath));
                    if (res.Abort)
                    {
                        HandleAbort(hook.ConsumerName, "PostBuild", res);
                        return res;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[VRCBuildPipelineHookManager] Error in PostBuild hook '{hook.ConsumerName}': {ex}");
                    HookResult errorRes = HookResult.Cancel($"Hook error in {hook.ConsumerName}: {ex.Message}", true);
                    HandleAbort(hook.ConsumerName, "PostBuild", errorRes);
                    return errorRes;
                }
            }
            return HookResult.Pass();
        }

        public static HookResult InvokePreUpload(GameObject avatarRoot, string thumbnailPath = null)
        {
            foreach (var hook in _preUploadHooks.OrderBy(h => h.Priority))
            {
                try
                {
                    HookResult res = hook.Callback((avatarRoot, thumbnailPath));
                    if (res.Abort)
                    {
                        HandleAbort(hook.ConsumerName, "PreUpload", res);
                        return res;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[VRCBuildPipelineHookManager] Error in PreUpload hook '{hook.ConsumerName}': {ex}");
                    HookResult errorRes = HookResult.Cancel($"Hook error in {hook.ConsumerName}: {ex.Message}", true);
                    HandleAbort(hook.ConsumerName, "PreUpload", errorRes);
                    return errorRes;
                }
            }
            return HookResult.Pass();
        }

        public static HookResult InvokePostUpload(GameObject avatarRoot, string thumbnailPath = null)
        {
            foreach (var hook in _postUploadHooks.OrderBy(h => h.Priority))
            {
                try
                {
                    HookResult res = hook.Callback((avatarRoot, thumbnailPath));
                    if (res.Abort)
                    {
                        HandleAbort(hook.ConsumerName, "PostUpload", res);
                        return res;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[VRCBuildPipelineHookManager] Error in PostUpload hook '{hook.ConsumerName}': {ex}");
                    HookResult errorRes = HookResult.Cancel($"Hook error in {hook.ConsumerName}: {ex.Message}", true);
                    HandleAbort(hook.ConsumerName, "PostUpload", errorRes);
                    return errorRes;
                }
            }
            return HookResult.Pass();
        }

        private static void HandleAbort(string consumerName, string phaseName, HookResult res)
        {
            string msg = !string.IsNullOrEmpty(res.ErrorMessage)
                ? res.ErrorMessage
                : $"VRChat build phase '{phaseName}' aborted by '{consumerName}'.";

            Debug.LogWarning($"[VRCBuildPipelineHookManager] Build phase '{phaseName}' aborted by '{consumerName}'. Message: {msg}");

            if (res.ShowDialog)
            {
                EditorUtility.DisplayDialog("VRChat Build Aborted", msg, "OK");
            }
        }

        // Native VRChat SDK Interface Implementations
        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            HookResult result = InvokePreprocessAvatar(avatarRoot);
            return !result.Abort;
        }

        public void OnPostprocessAvatar(GameObject avatarRoot)
        {
            InvokePostprocessAvatar(avatarRoot);
        }

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            HookResult result = InvokeBuildRequested(requestedBuildType);
            return !result.Abort;
        }
    }
}
