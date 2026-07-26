using System.Collections.Generic;
using UnityEngine;

namespace Bluscream.VRCAvatarOptimizer
{
    public abstract class PlatformProfile_Android : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.Android;
        public override string PlatformSuffix => " (Quest)";
        public override long MaxAssetBundleSizeBytes => 10 * 1024 * 1024L; // VRChat Quest 10 MB hard cap

        protected PlatformProfile_Android()
        {
            MaxContacts = 16; // VRChat Quest VRCContact hard cap

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

            // VRCContact components are pruned separately via MaxContacts limit, not via blacklist
            if (t.Name.Contains("VRCContact")) return false;
            return base.ShouldRemoveComponentCustom(comp);
        }

        public override void ExecutePlatformConversions(GameObject avatarRoot, System.Action<string> progressCallback = null)
        {
            progressCallback?.Invoke("Executing Android/Quest platform-specific conversions...");
            // Additional Android/Quest specific logic (e.g., stripping lightmaps, enforcing GPU instancing)
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

            // Asset bundle size check — calls back from VRCAvatarOptimizerCore via profile.MaxAssetBundleSizeBytes
        }
    }
}
