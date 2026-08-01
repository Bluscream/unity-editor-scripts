using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static Bluscream.Utils;
using static Bluscream.TransformExtensions;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Dedicated optimizer for VRChat Contact Sender and Receiver components.
    /// </summary>
    public static class AvatarContactOptimizer
    {
        /// <summary>
        /// Prunes excess VRCContactSender and VRCContactReceiver components to fit within profile.MaxContacts.
        /// </summary>
        public static int PruneContacts(GameObject avatarRoot, int maxContacts, Action<string> progressCallback = null)
        {
            if (avatarRoot == null) return 0;

            List<Component> contactComps = avatarRoot.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && (c.GetType().Name.Contains("VRCContactSender") || c.GetType().Name.Contains("VRCContactReceiver")))
                // Shallowest first: the loop below prunes the tail, so deep accessory/detail components
                // are dropped before ones near the avatar root. Mirrors AvatarPhysBonePruner's ordering.
                .OrderBy(c => c.transform.GetHierarchyDepth())
                .ToList();

            if (contactComps.Count <= maxContacts) return 0;

            int prunedCount = contactComps.Count - maxContacts;
            Debug.Log($"[AvatarContactOptimizer] VRCContact components: {contactComps.Count} > {maxContacts} limit. Pruning {prunedCount}.");
            progressCallback?.Invoke($"Pruning excess VRCContact components ({contactComps.Count} -> {maxContacts})...");

            for (int i = maxContacts; i < contactComps.Count; i++)
            {
                Component c = contactComps[i];
                if (c != null)
                {
                    Debug.Log($"[AvatarContactOptimizer] Pruning '{c.GetType().Name}' from '{GetGameObjectPath(c.gameObject)}'");
                    Undo.DestroyObjectImmediate(c);
                }
            }

            return prunedCount;
        }
    }
}
