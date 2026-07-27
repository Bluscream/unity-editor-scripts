using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public abstract class PlatformProfile_Android : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.Android;
        // 10 MB compressed mobile cap (verified against VRC.ValidationHelpers.GetAssetBundleSizeLimit);
        // read live from the SDK when available so SDK updates are picked up automatically.
        public override long MaxAssetBundleSizeBytes => GetSdkAssetBundleSizeLimit(isMobilePlatform: true, fallbackBytes: 10 * 1024 * 1024L);

        protected override HashSet<string> CreateBlacklist() => new HashSet<string>(new[]
        {
            "Cloth", "Camera", "Light", "AudioSource", "Rigidbody",
            "Collider", "BoxCollider", "SphereCollider", "CapsuleCollider", "MeshCollider",
            "Joint", "SpringJoint", "HingeJoint", "FixedJoint", "CharacterJoint", "ConfigurableJoint",
            "ParticleSystem", "DynamicBone", "DynamicBoneCollider",
            "VRCSpatialAudioSource", "FinalIK", "PostProcessLayer", "PostProcessVolume"
        }, StringComparer.OrdinalIgnoreCase);

        public override bool ShouldRemoveComponentCustom(Component comp)
        {
            if (comp == null) return false;

            Type compType = comp.GetType();
            string typeName = compType.Name;
            string typeNameLower = (compType.FullName ?? typeName).ToLowerInvariant();

            // VRCContact components are pruned separately via MaxContacts; not removed via blacklist
            if (typeName.Contains("VRCContact")) return false;

            // Components that are never allowed on VRChat Mobile avatars (but legal on PC):
            if (comp is Camera) return true;
            if (comp is Joint) return true;
            if (typeNameLower.Contains("dynamicbone")) return true;
            if (typeNameLower.Contains("finalik") || typeNameLower.Contains("rootmotion")) return true;
            if (typeNameLower.Contains("postprocess")) return true;
            // Unity constraints are not mobile-whitelisted; VRChat constraints are and get pruned via MaxConstraints
            if (typeNameLower.Contains("constraint") && !typeNameLower.Contains("vrc")) return true;

            return base.ShouldRemoveComponentCustom(comp);
        }

        public override void ExecutePlatformConversions(GameObject avatarRoot, System.Action<string> progressCallback = null)
        {
            progressCallback?.Invoke($"Executing {Platform} mobile platform-specific conversions...");
            
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
            base.ValidatePlatformRules(avatarRoot, summary);

            // Material slot limit check
            var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r != null && r.sharedMaterials != null && r.sharedMaterials.Length > 4)
                    summary.AddWarning($"Renderer '{r.name}' has {r.sharedMaterials.Length} material slots (VRChat Mobile limit is 4).", r);
            }
        }
    }
}
