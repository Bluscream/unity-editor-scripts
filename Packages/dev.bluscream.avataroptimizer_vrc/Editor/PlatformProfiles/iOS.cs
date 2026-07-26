using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    /// <summary>
    /// Base class for iOS mobile platform profiles.
    /// Inherits identical limits from Android Quest: PhysBones (8), Transforms (64), Colliders (16),
    /// Collision Checks (64), Contacts (16), Material Slots (4), Asset Bundle 10 MB.
    /// VRChat falls back to Android build if no iOS-specific upload is present.
    /// </summary>
    public abstract class PlatformProfile_iOS : PlatformProfile_Android
    {
        public override TargetPlatform Platform => TargetPlatform.iOS;
        // MaxContacts, MaxAssetBundleSizeBytes, BlacklistedComponentNames, etc. all inherited from PlatformProfile_Android

        public override void ExecutePlatformConversions(GameObject avatarRoot, System.Action<string> progressCallback = null)
        {
            progressCallback?.Invoke("Executing iOS mobile platform-specific conversions...");
        }

        public override void ValidatePlatformRules(GameObject avatarRoot, ConversionSummary summary)
        {
            if (avatarRoot == null || summary == null) return;

            var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r != null && r.sharedMaterials != null && r.sharedMaterials.Length > 4)
                    summary.AddWarning($"Renderer '{r.name}' has {r.sharedMaterials.Length} material slots (VRChat Mobile iOS limit is 4).", r);
            }
        }
    }
}
