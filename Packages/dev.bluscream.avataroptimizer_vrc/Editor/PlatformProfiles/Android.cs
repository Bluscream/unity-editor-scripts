using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public abstract class PlatformProfile_Android : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.Android;
        public override int MaxContacts => 16;                               // VRChat Quest VRCContact hard cap
        public override long MaxAssetBundleSizeBytes => 10 * 1024 * 1024L;  // VRChat Quest 10 MB hard cap

        protected override HashSet<string> CreateBlacklist() => new HashSet<string>(new[]
        {
            "Cloth", "Camera", "Light", "AudioSource", "Rigidbody",
            "Joint", "SpringJoint", "HingeJoint", "FixedJoint", "CharacterJoint", "ConfigurableJoint",
            "ParticleSystem", "DynamicBone", "DynamicBoneCollider",
            "VRCSpatialAudioSource", "FinalIK", "PostProcessLayer", "PostProcessVolume"
        }, StringComparer.OrdinalIgnoreCase);

        public override bool ShouldRemoveComponentCustom(Component comp)
        {
            if (comp == null) return false;
            // VRCContact components are pruned separately via MaxContacts; not removed via blacklist
            if (comp.GetType().Name.Contains("VRCContact")) return false;
            return base.ShouldRemoveComponentCustom(comp);
        }

        public override void ExecutePlatformConversions(GameObject avatarRoot, System.Action<string> progressCallback = null)
        {
            progressCallback?.Invoke("Executing Android/Quest platform-specific conversions...");
            
            // Enforce VRChat Mobile Quality Setting: Pixel Light Count <= 1 (prevents VRChat SDK build error)
            if (QualitySettings.pixelLightCount > 1)
            {
                Debug.Log($"[PlatformProfile_Android] Adjusting QualitySettings.pixelLightCount from {QualitySettings.pixelLightCount} -> 1 for VRChat Mobile compliance.");
                QualitySettings.pixelLightCount = 1;
            }
        }

        public override void ValidatePlatformRules(GameObject avatarRoot, ConversionSummary summary)
        {
            if (avatarRoot == null || summary == null) return;

            // Material slot limit check
            var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r != null && r.sharedMaterials != null && r.sharedMaterials.Length > 4)
                    summary.AddWarning($"Renderer '{r.name}' has {r.sharedMaterials.Length} material slots (Android Quest limit is 4).", r);
            }
        }
    }
}
