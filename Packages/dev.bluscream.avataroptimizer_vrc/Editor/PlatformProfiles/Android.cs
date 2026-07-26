using System.Collections.Generic;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public abstract class PlatformProfile_Android : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.Android;

        protected PlatformProfile_Android()
        {
            // All Android profiles strictly blacklist components prohibited by VRChat Quest SDK policy
            BlacklistedComponentNames.UnionWith(new[] {
                "Cloth", "Camera", "Light", "AudioSource", "Rigidbody", "Joint", "SpringJoint", "HingeJoint",
                "FixedJoint", "CharacterJoint", "ConfigurableJoint", "ParticleSystem", "DynamicBone",
                "DynamicBoneCollider", "VRCSpatialAudioSource", "FinalIK", "PostProcessLayer", "PostProcessVolume"
            });
        }

        public override bool ShouldRemoveComponentCustom(Component comp)
        {
            if (comp == null) return false;
            System.Type t = comp.GetType();

            // Mobile-specific custom checks: remove custom post-processing & non-mobile shaders
            if (t.Name.Contains("PostProcess") || t.Name.Contains("VRCContact")) return false; // Handled separately
            return base.ShouldRemoveComponentCustom(comp);
        }

        public override void ExecutePlatformConversions(GameObject avatarRoot, System.Action<string> progressCallback = null)
        {
            progressCallback?.Invoke("Executing Android platform-specific conversions...");
            // Additional Android/Quest specific logic (e.g., stripping lightmaps, enforcing GPU instancing)
        }

        public override void ValidatePlatformRules(GameObject avatarRoot, ConversionSummary summary)
        {
            if (avatarRoot == null || summary == null) return;
            // Android platform validation checks
            var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r != null && r.sharedMaterials != null && r.sharedMaterials.Length > 4)
                {
                    summary.AddWarning($"Renderer '{r.name}' has {r.sharedMaterials.Length} material slots (Android Quest limit is 4 per avatar).", r);
                }
            }
        }
    }
}
