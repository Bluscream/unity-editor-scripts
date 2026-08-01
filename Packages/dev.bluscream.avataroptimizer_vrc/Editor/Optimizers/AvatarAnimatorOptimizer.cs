using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Optimizes AnimatorControllers (especially FX Layer) by combining simple 2-state toggle layers
    /// into a single Direct Blend Tree, purging useless/empty layers, and converting toggle parameters to Float.
    /// </summary>
    public static class AvatarAnimatorOptimizer
    {
        public static void OptimizeAnimatorControllers(GameObject avatarRoot, Action<string> progressCallback = null)
        {
            if (avatarRoot == null) return;

            Animator[] animators = avatarRoot.GetComponentsInChildren<Animator>(true);
            foreach (Animator anim in animators)
            {
                if (anim == null || anim.runtimeAnimatorController == null) continue;
                AnimatorController controller = anim.runtimeAnimatorController as AnimatorController;
                if (controller == null) continue;

                progressCallback?.Invoke($"Optimizing Animator Controller '{controller.name}'...");
                OptimizeController(controller);
            }
        }

        public static void OptimizeController(AnimatorController controller)
        {
            if (controller == null || controller.layers.Length < 2) return;

            var layers = controller.layers.ToList();
            var parameters = controller.parameters.ToList();
            HashSet<string> floatParams = new HashSet<string>(parameters.Where(p => p.type == AnimatorControllerParameterType.Float).Select(p => p.name));

            List<int> layersToMerge = new List<int>();
            List<int> layersToRemove = new List<int>();

            // Layer 0 is base layer — do not merge. Protect layers 1-2 if MMD layers.
            for (int i = 1; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer.stateMachine == null)
                {
                    layersToRemove.Add(i);
                    continue;
                }

                var stateMachine = layer.stateMachine;
                // Dead layer check: empty state machine or 0 states
                if (stateMachine.states.Length == 0 && stateMachine.stateMachines.Length == 0)
                {
                    layersToRemove.Add(i);
                    continue;
                }

                // A mask, a non-default weight or a non-override blend mode cannot be expressed inside a
                // shared Direct Blend Tree, so those layers keep their own layer.
                if (layer.avatarMask != null)
                {
                    Debug.Log($"[AvatarAnimatorOptimizer] Layer '{layer.name}' has an avatar mask — left as its own layer.");
                    continue;
                }
                if (layer.blendingMode != AnimatorLayerBlendingMode.Override)
                {
                    Debug.Log($"[AvatarAnimatorOptimizer] Layer '{layer.name}' uses {layer.blendingMode} blending — left as its own layer.");
                    continue;
                }
                if (!Mathf.Approximately(layer.defaultWeight, 1f))
                {
                    Debug.Log($"[AvatarAnimatorOptimizer] Layer '{layer.name}' has default weight {layer.defaultWeight} — left as its own layer.");
                    continue;
                }

                // Check for simple 2-state toggle layer
                if (IsSimpleToggleLayer(stateMachine, out string paramName, out Motion offMotion, out Motion onMotion))
                {
                    // Convert Bool or Int parameter to Float if needed
                    EnsureParameterIsFloat(controller, paramName, floatParams);
                    layersToMerge.Add(i);
                }
            }

            if (layersToMerge.Count == 0 && layersToRemove.Count == 0) return;

            Undo.RecordObject(controller, "Optimize FX Animator Controller");

            // Build Direct Blend Tree for combined toggles if any eligible layers found
            if (layersToMerge.Count > 0)
            {
                BlendTree directBlendTree = new BlendTree();
                directBlendTree.name = "Combined_DirectBlendTree_Toggles";
                directBlendTree.blendType = BlendTreeType.Direct;

                // Every child of a Direct tree is weighted by its own parameter. Weighting a toggle's
                // motion directly by the toggle parameter would leave the off motion undriven at 0, so
                // each toggle instead becomes a 1D tree (off at 0, on at 1) held at full weight by a
                // constant-1 parameter. That reproduces the original layer's behaviour on Write Defaults
                // Off avatars, where the off state has to actively animate properties back.
                string constantOneParam = EnsureConstantOneParameter(controller, floatParams);

                List<ChildMotion> childMotions = new List<ChildMotion>();

                foreach (int layerIdx in layersToMerge)
                {
                    var layer = layers[layerIdx];
                    if (!IsSimpleToggleLayer(layer.stateMachine, out string paramName, out Motion offMotion, out Motion onMotion))
                        continue;

                    BlendTree toggleTree = new BlendTree
                    {
                        name = $"Toggle_{paramName}",
                        blendType = BlendTreeType.Simple1D,
                        blendParameter = paramName,
                        useAutomaticThresholds = false
                    };
                    AssetDatabase.AddObjectToAsset(toggleTree, controller);

                    toggleTree.children = new[]
                    {
                        new ChildMotion { motion = offMotion, threshold = 0f, timeScale = 1f },
                        new ChildMotion { motion = onMotion,  threshold = 1f, timeScale = 1f }
                    };

                    childMotions.Add(new ChildMotion
                    {
                        motion = toggleTree,
                        directBlendParameter = constantOneParam,
                        timeScale = 1.0f
                    });
                }

                directBlendTree.children = childMotions.ToArray();

                // Create a new layer for the Direct Blend Tree
                AnimatorControllerLayer dbtLayer = new AnimatorControllerLayer
                {
                    name = "Optimized_DirectBlendTree",
                    defaultWeight = 1.0f,
                    stateMachine = new AnimatorStateMachine
                    {
                        name = "Optimized_DirectBlendTree_SM",
                        hideFlags = HideFlags.HideInHierarchy
                    }
                };

                AssetDatabase.AddObjectToAsset(dbtLayer.stateMachine, controller);
                AssetDatabase.AddObjectToAsset(directBlendTree, controller);

                AnimatorState dbtState = dbtLayer.stateMachine.AddState("DirectBlendTree");
                dbtState.motion = directBlendTree;
                dbtLayer.stateMachine.defaultState = dbtState;

                // Add DBT layer at index 1
                layers.Insert(1, dbtLayer);
                Debug.Log($"[AvatarAnimatorOptimizer] Merged {layersToMerge.Count} toggle layers into Direct Blend Tree on '{controller.name}'.");
            }

            // Remove merged and dead layers (in reverse order to preserve indices)
            HashSet<int> allIndicesToRemove = new HashSet<int>(layersToMerge.Concat(layersToRemove));
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                if (allIndicesToRemove.Contains(i))
                {
                    layers.RemoveAt(i);
                }
            }

            controller.layers = layers.ToArray();
            EditorUtility.SetDirty(controller);
        }

        private static bool IsSimpleToggleLayer(AnimatorStateMachine sm, out string paramName, out Motion offMotion, out Motion onMotion)
        {
            paramName = null;
            offMotion = null;
            onMotion = null;

            if (sm == null || sm.states.Length != 2 || sm.stateMachines.Length > 0) return false;

            var s0 = sm.states[0].state;
            var s1 = sm.states[1].state;
            if (s0 == null || s1 == null) return false;

            // Check if states have transitions to each other
            var t01 = s0.transitions.FirstOrDefault(t => t.destinationState == s1);
            var t10 = s1.transitions.FirstOrDefault(t => t.destinationState == s0);

            if (t01 == null || t10 == null) return false;
            if (t01.conditions.Length != 1 || t10.conditions.Length != 1) return false;

            AnimatorCondition c01 = t01.conditions[0];
            if (c01.parameter != t10.conditions[0].parameter) return false;

            paramName = c01.parameter;
            if (string.IsNullOrEmpty(paramName)) return false;

            // sm.states order is serialization order, not semantics — read the direction from the
            // condition instead, or the toggle silently inverts.
            bool s0IsOff = ConditionMeansOn(c01);
            if (!s0IsOff && !ConditionMeansOff(c01)) return false; // unrecognised condition shape

            offMotion = s0IsOff ? s0.motion : s1.motion;
            onMotion = s0IsOff ? s1.motion : s0.motion;

            return true;
        }

        /// <summary>True when satisfying this condition moves toward the enabled state.</summary>
        private static bool ConditionMeansOn(AnimatorCondition c)
        {
            switch (c.mode)
            {
                case AnimatorConditionMode.If: return true;
                case AnimatorConditionMode.Greater: return true;
                case AnimatorConditionMode.Equals: return c.threshold != 0f;
                default: return false;
            }
        }

        private static bool ConditionMeansOff(AnimatorCondition c)
        {
            switch (c.mode)
            {
                case AnimatorConditionMode.IfNot: return true;
                case AnimatorConditionMode.Less: return true;
                case AnimatorConditionMode.NotEqual: return c.threshold != 0f;
                default: return false;
            }
        }

        /// <summary>
        /// Finds or creates the float parameter that holds a Direct Blend Tree child at full weight.
        /// </summary>
        private static string EnsureConstantOneParameter(AnimatorController controller, HashSet<string> floatParams)
        {
            const string ConstantOneName = "OptimizerConstantOne";

            foreach (var p in controller.parameters)
            {
                if (p.name != ConstantOneName) continue;
                if (p.type == AnimatorControllerParameterType.Float && Mathf.Approximately(p.defaultFloat, 1f))
                    return ConstantOneName;
                break;
            }

            controller.AddParameter(new AnimatorControllerParameter
            {
                name = ConstantOneName,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f
            });
            floatParams.Add(ConstantOneName);

            Debug.Log($"[AvatarAnimatorOptimizer] Added '{ConstantOneName}' (float, default 1) to drive the Direct Blend Tree on '{controller.name}'.");
            return ConstantOneName;
        }

        private static void EnsureParameterIsFloat(AnimatorController controller, string paramName, HashSet<string> floatParams)
        {
            if (floatParams.Contains(paramName)) return;

            for (int i = 0; i < controller.parameters.Length; i++)
            {
                var p = controller.parameters[i];
                if (p.name == paramName && p.type != AnimatorControllerParameterType.Float)
                {
                    // Carry the default across, or a toggle that defaulted to on comes back defaulting to off.
                    float defaultVal = p.type == AnimatorControllerParameterType.Bool ? (p.defaultBool ? 1f : 0f) : p.defaultInt;
                    controller.RemoveParameter(i);
                    controller.AddParameter(new AnimatorControllerParameter
                    {
                        name = paramName,
                        type = AnimatorControllerParameterType.Float,
                        defaultFloat = defaultVal
                    });
                    floatParams.Add(paramName);
                    break;
                }
            }
        }
    }
}
