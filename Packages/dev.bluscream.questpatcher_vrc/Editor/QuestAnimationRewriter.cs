using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace VRCQuestPatcher
{
    /// <summary>
    /// Rewrites AnimatorControllers, AnimationClips, Material Swaps, Flipbooks, and VRCFury components
    /// so they point to duplicated Quest materials and shaders.
    /// </summary>
    public static class QuestAnimationRewriter
    {
        public static void ProcessAvatarAnimationsAndVRCFury(
            GameObject avatarRoot, 
            Dictionary<Material, Material> materialMap,
            string outputDirectory,
            Action<string> progressCallback = null)
        {
            if (avatarRoot == null || materialMap == null || materialMap.Count == 0) return;

            HashSet<AnimationClip> processedClips = new HashSet<AnimationClip>();
            Dictionary<AnimationClip, AnimationClip> clipMap = new Dictionary<AnimationClip, AnimationClip>();
            Dictionary<RuntimeAnimatorController, RuntimeAnimatorController> controllerMap = new Dictionary<RuntimeAnimatorController, RuntimeAnimatorController>();

            // 1. Process standard Animator components
            Animator[] animators = avatarRoot.GetComponentsInChildren<Animator>(true);
            foreach (Animator anim in animators)
            {
                if (anim == null || anim.runtimeAnimatorController == null) continue;

                RuntimeAnimatorController newController = ProcessController(
                    anim.runtimeAnimatorController, 
                    materialMap, 
                    clipMap, 
                    controllerMap, 
                    outputDirectory, 
                    progressCallback
                );

                if (newController != null && newController != anim.runtimeAnimatorController)
                {
                    Undo.RecordObject(anim, "Assign Quest Animator Controller");
                    anim.runtimeAnimatorController = newController;
                }
            }

            // 2. Process all MonoBehaviours (including VRCFury components) via SerializedObject
            Component[] components = avatarRoot.GetComponentsInChildren<Component>(true);
            foreach (Component comp in components)
            {
                if (comp == null || comp is Transform || comp is Renderer || comp is Animator) continue;

                try
                {
                    SerializedObject so = new SerializedObject(comp);
                    SerializedProperty prop = so.GetIterator();
                    bool modified = false;

                    while (prop.NextVisible(true))
                    {
                        if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue != null)
                        {
                            // Material remap
                            if (prop.objectReferenceValue is Material sourceMat && materialMap.TryGetValue(sourceMat, out Material questMat))
                            {
                                prop.objectReferenceValue = questMat;
                                modified = true;
                                progressCallback?.Invoke($"Remapped material reference on {comp.GetType().Name} ({comp.gameObject.name})");
                            }
                            // AnimationClip remap
                            else if (prop.objectReferenceValue is AnimationClip sourceClip)
                            {
                                AnimationClip questClip = ProcessClip(sourceClip, materialMap, clipMap, outputDirectory, progressCallback);
                                if (questClip != sourceClip)
                                {
                                    prop.objectReferenceValue = questClip;
                                    modified = true;
                                    progressCallback?.Invoke($"Remapped animation clip reference on {comp.GetType().Name} ({comp.gameObject.name})");
                                }
                            }
                            // AnimatorController remap
                            else if (prop.objectReferenceValue is RuntimeAnimatorController sourceController)
                            {
                                RuntimeAnimatorController questController = ProcessController(sourceController, materialMap, clipMap, controllerMap, outputDirectory, progressCallback);
                                if (questController != sourceController)
                                {
                                    prop.objectReferenceValue = questController;
                                    modified = true;
                                    progressCallback?.Invoke($"Remapped animator controller on {comp.GetType().Name} ({comp.gameObject.name})");
                                }
                            }
                        }
                    }

                    if (modified)
                    {
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(comp);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[QuestAnimationRewriter] Failed inspecting component {comp.GetType().Name}: {e.Message}");
                }
            }
        }

        private static RuntimeAnimatorController ProcessController(
            RuntimeAnimatorController sourceController,
            Dictionary<Material, Material> materialMap,
            Dictionary<AnimationClip, AnimationClip> clipMap,
            Dictionary<RuntimeAnimatorController, RuntimeAnimatorController> controllerMap,
            string outputDirectory,
            Action<string> progressCallback)
        {
            if (sourceController == null) return null;

            if (controllerMap.TryGetValue(sourceController, out RuntimeAnimatorController existing))
                return existing;

            AnimatorController ac = sourceController as AnimatorController;
            if (ac == null) return sourceController;

            // Check if any animation clips inside this controller need remapping
            bool needsCopy = false;
            AnimationClip[] clips = ac.animationClips;
            foreach (AnimationClip clip in clips)
            {
                if (ClipHasMaterialBindings(clip, materialMap))
                {
                    needsCopy = true;
                    break;
                }
            }

            if (!needsCopy)
            {
                controllerMap[sourceController] = sourceController;
                return sourceController;
            }

            // Duplicate Controller
            string sourcePath = AssetDatabase.GetAssetPath(sourceController);
            string destPath = GetQuestAssetPath(sourcePath, "controller", outputDirectory);

            AssetDatabase.CopyAsset(sourcePath, destPath);
            AnimatorController newAc = AssetDatabase.LoadAssetAtPath<AnimatorController>(destPath);

            if (newAc != null)
            {
                // Remap clips inside states
                foreach (AnimatorControllerLayer layer in newAc.layers)
                {
                    RemapStateMachineClips(layer.stateMachine, materialMap, clipMap, outputDirectory, progressCallback);
                }

                EditorUtility.SetDirty(newAc);
                controllerMap[sourceController] = newAc;
                return newAc;
            }

            return sourceController;
        }

        private static void RemapStateMachineClips(
            AnimatorStateMachine stateMachine,
            Dictionary<Material, Material> materialMap,
            Dictionary<AnimationClip, AnimationClip> clipMap,
            string outputDirectory,
            Action<string> progressCallback)
        {
            if (stateMachine == null) return;

            foreach (ChildAnimatorState state in stateMachine.states)
            {
                if (state.state.motion is AnimationClip clip)
                {
                    state.state.motion = ProcessClip(clip, materialMap, clipMap, outputDirectory, progressCallback);
                }
                else if (state.state.motion is BlendTree tree)
                {
                    RemapBlendTreeClips(tree, materialMap, clipMap, outputDirectory, progressCallback);
                }
            }

            foreach (ChildAnimatorStateMachine subMachine in stateMachine.stateMachines)
            {
                RemapStateMachineClips(subMachine.stateMachine, materialMap, clipMap, outputDirectory, progressCallback);
            }
        }

        private static void RemapBlendTreeClips(
            BlendTree tree,
            Dictionary<Material, Material> materialMap,
            Dictionary<AnimationClip, AnimationClip> clipMap,
            string outputDirectory,
            Action<string> progressCallback)
        {
            if (tree == null) return;

            ChildMotion[] children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].motion is AnimationClip clip)
                {
                    children[i].motion = ProcessClip(clip, materialMap, clipMap, outputDirectory, progressCallback);
                }
                else if (children[i].motion is BlendTree subTree)
                {
                    RemapBlendTreeClips(subTree, materialMap, clipMap, outputDirectory, progressCallback);
                }
            }
            tree.children = children;
        }

        private static AnimationClip ProcessClip(
            AnimationClip sourceClip,
            Dictionary<Material, Material> materialMap,
            Dictionary<AnimationClip, AnimationClip> clipMap,
            string outputDirectory,
            Action<string> progressCallback)
        {
            if (sourceClip == null) return null;

            if (clipMap.TryGetValue(sourceClip, out AnimationClip existing))
                return existing;

            if (!ClipHasMaterialBindings(sourceClip, materialMap))
            {
                clipMap[sourceClip] = sourceClip;
                return sourceClip;
            }

            // Duplicate AnimationClip
            string sourcePath = AssetDatabase.GetAssetPath(sourceClip);
            string destPath = GetQuestAssetPath(sourcePath, "anim", outputDirectory);

            AssetDatabase.CopyAsset(sourcePath, destPath);
            AnimationClip newClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(destPath);

            if (newClip != null)
            {
                // Remap Object Reference Curves (Material swaps/toggles/flipbooks)
                EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(newClip);
                foreach (EditorCurveBinding binding in bindings)
                {
                    ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(newClip, binding);
                    bool keyframeModified = false;

                    for (int i = 0; i < keyframes.Length; i++)
                    {
                        if (keyframes[i].value is Material srcMat && materialMap.TryGetValue(srcMat, out Material questMat))
                        {
                            keyframes[i].value = questMat;
                            keyframeModified = true;
                        }
                    }

                    if (keyframeModified)
                    {
                        AnimationUtility.SetObjectReferenceCurve(newClip, binding, keyframes);
                    }
                }

                EditorUtility.SetDirty(newClip);
                clipMap[sourceClip] = newClip;
                progressCallback?.Invoke($"Created Quest animation clip copy: {newClip.name}");
                return newClip;
            }

            return sourceClip;
        }

        private static bool ClipHasMaterialBindings(AnimationClip clip, Dictionary<Material, Material> materialMap)
        {
            if (clip == null) return false;
            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            foreach (EditorCurveBinding binding in bindings)
            {
                ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                foreach (var kf in keyframes)
                {
                    if (kf.value is Material mat && materialMap.ContainsKey(mat))
                        return true;
                }
            }
            return false;
        }

        private static string GetQuestAssetPath(string sourcePath, string defaultExt, string outputDirectory)
        {
            string dir = !string.IsNullOrEmpty(outputDirectory) && Directory.Exists(outputDirectory)
                ? outputDirectory
                : Path.GetDirectoryName(sourcePath);

            string filename = Path.GetFileNameWithoutExtension(sourcePath);
            string ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrEmpty(ext)) ext = "." + defaultExt;

            string targetName = filename.EndsWith(" (Quest)") ? filename : filename + " (Quest)";
            return Path.Combine(dir, targetName + ext).Replace('\\', '/');
        }
    }
}
