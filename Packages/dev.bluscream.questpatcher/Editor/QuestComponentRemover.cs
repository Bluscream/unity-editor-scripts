using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static Bluscream.Utils;

namespace VRCQuestPatcher
{
    /// <summary>
    /// Removes Quest-incompatible components from avatars
    /// </summary>
    public static class QuestComponentRemover
    {
        public class RemovedComponent
        {
            public GameObject gameObject;
            public string componentType;
            public string gameObjectPath;
        }

        /// <summary>
        /// Removes all Quest-incompatible components from the avatar
        /// </summary>
        public static List<RemovedComponent> RemoveIncompatibleComponents(GameObject avatarRoot, System.Action<string> progressCallback = null)
        {
            List<RemovedComponent> removed = new List<RemovedComponent>();
            
            if (avatarRoot == null)
            {
                Debug.LogError("Avatar root is null");
                return removed;
            }

            // Get all GameObjects recursively
            List<GameObject> allGameObjects = new List<GameObject>();
            CollectAllGameObjects(avatarRoot.transform, allGameObjects);

            int total = allGameObjects.Count;
            for (int i = 0; i < allGameObjects.Count; i++)
            {
                GameObject go = allGameObjects[i];
                if (go == null) continue;

                progressCallback?.Invoke($"Removing incompatible components ({i + 1}/{total})...");

                Component[] components = go.GetComponents<Component>();
                
                // First pass: Remove dependent components (like VRCSpatialAudioSource before AudioSource)
                List<Component> toRemove = new List<Component>();
                foreach (Component comp in components)
                {
                    if (comp == null) continue;
                    
                    // Check if this is a VRChat component that depends on incompatible components
                    string typeName = comp.GetType().FullName;
                    if (typeName != null && typeName.Contains("VRCSpatialAudioSource"))
                    {
                        toRemove.Add(comp);
                    }
                }
                
                // Remove dependent components first
                foreach (Component comp in toRemove)
                {
                    try
                    {
                        Undo.RegisterCompleteObjectUndo(go, "Remove Quest-incompatible component");
                        if (Application.isPlaying)
                            UnityEngine.Object.Destroy(comp);
                        else
                            UnityEngine.Object.DestroyImmediate(comp, true);
                        
                        removed.Add(new RemovedComponent
                        {
                            gameObject = go,
                            componentType = comp.GetType().FullName,
                            gameObjectPath = GetGameObjectPath(go)
                        });
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Failed to remove component {comp.GetType().Name} from {go.name}: {e.Message}");
                    }
                }
                
                // Second pass: Remove incompatible components
                components = go.GetComponents<Component>(); // Refresh after removals
                foreach (Component comp in components)
                {
                    if (comp == null) continue;

                    if (ShouldRemoveComponent(comp))
                    {
                        RemovedComponent removedComp = new RemovedComponent
                        {
                            gameObject = go,
                            componentType = comp.GetType().FullName,
                            gameObjectPath = GetGameObjectPath(go)
                        };

                        try
                        {
                            Undo.RegisterCompleteObjectUndo(go, "Remove Quest-incompatible component");
                            
                            // Use DestroyImmediate for editor, Destroy for runtime
                            if (Application.isPlaying)
                                UnityEngine.Object.Destroy(comp);
                            else
                                UnityEngine.Object.DestroyImmediate(comp, true);
                            
                            removed.Add(removedComp);
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"Failed to remove component {comp.GetType().Name} from {go.name}: {e.Message}");
                        }
                    }
                }
            }

            return removed;
        }

        /// <summary>
        /// Determines if a component should be removed for Quest compatibility
        /// </summary>
        private static bool ShouldRemoveComponent(Component comp)
        {
            if (comp == null) return false;

            Type compType = comp.GetType();
            string typeName = compType.FullName;
            string typeNameLower = typeName.ToLowerInvariant();

            // Dynamic Bones
            if (typeNameLower.Contains("dynamicbone") || compType.Name.Contains("DynamicBone"))
                return true;

            // Cloth
            if (comp is Cloth)
                return true;

            // Camera (only on avatars, not worlds)
            if (comp is Camera)
                return true;

            // Light (only on avatars, not worlds)
            if (comp is Light)
                return true;

            // AudioSource (only on avatars, not worlds)
            if (comp is AudioSource)
                return true;

            // Physics components (only on avatars)
            if (comp is Rigidbody)
                return true;

            if (comp is Collider)
            {
                // Allow colliders that are part of VRChat systems (like PhysBones)
                // But remove standalone colliders
                return true; // Will be refined based on VRChat SDK detection
            }

            // Joints
            if (comp is Joint || compType.IsSubclassOf(typeof(Joint)))
                return true;

            // Particle Systems (with limits, but we'll remove for safety)
            // Note: VRChat allows limited particles, but for simplicity we remove all
            if (comp is ParticleSystem)
                return true;

            // Unity Constraints
            if (typeNameLower.Contains("constraint") && !typeNameLower.Contains("vrchat"))
                return true;

            // FinalIK
            if (typeNameLower.Contains("finalik") || typeNameLower.Contains("rootmotion.finalik"))
                return true;

            // Post-processing components
            if (typeNameLower.Contains("postprocess") || typeNameLower.Contains("postprocesslayer"))
                return true;

            return false;
        }

        /// <summary>
        /// Collects all GameObjects recursively
        /// </summary>
        private static void CollectAllGameObjects(Transform parent, List<GameObject> collection)
        {
            if (parent == null) return;

            collection.Add(parent.gameObject);

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child != null)
                {
                    CollectAllGameObjects(child, collection);
                }
            }
        }

    }
}
