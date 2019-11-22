using Unity.Build;

class RetainBlobAssetsSetting : IBuildSettingsComponent
{
    public int FramesToRetainBlobAssets;

    public string Name => "RetainBlobAssetsSetting";

    public static int GetFramesToRetainBlobAssets(BuildSettings buildSettings)
    {
        int framesToRetainBlobAssets = 1;
        if (buildSettings != null && buildSettings.TryGetComponent(out RetainBlobAssetsSetting retainSetting))
            framesToRetainBlobAssets = retainSetting.FramesToRetainBlobAssets;
        return framesToRetainBlobAssets;
    }
}
