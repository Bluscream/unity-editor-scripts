namespace Bluscream.VRCAvatarOptimizer
{
    public abstract class PlatformProfile_PC : PlatformProfile
    {
        public override TargetPlatform Platform => TargetPlatform.PC;

        protected PlatformProfile_PC()
        {
            // 200 MB compressed PC cap (verified against VRC.ValidationHelpers.GetAssetBundleSizeLimit);
            // read live from the SDK when available so SDK updates are picked up automatically.
            MaxAssetBundleSizeBytes = GetSdkAssetBundleSizeLimit(isMobilePlatform: false, fallbackBytes: 200 * 1024 * 1024L); // 200 MB
        }
    }
}
