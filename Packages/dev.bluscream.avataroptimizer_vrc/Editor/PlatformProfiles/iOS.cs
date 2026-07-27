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
        // MaxContacts, MaxAssetBundleSizeBytes, BlacklistedComponentNames, platform conversions,
        // and validation rules are all inherited from PlatformProfile_Android.
    }
}
