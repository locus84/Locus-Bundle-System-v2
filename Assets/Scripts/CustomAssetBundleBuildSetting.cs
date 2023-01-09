using System.Collections.Generic;
using UnityEngine;
using BundleSystem;

[CreateAssetMenu(fileName = "AssetBundleBuildSetting.asset", menuName = "Create Custom AssetBundle Build Setting", order = 999)]
public class CustomAssetBundleBuildSetting : AssetBundleBuildSetting
{
    public override List<BundleSetting> GetBundleSettings()
    {
        //add settings manually
        var bundleSettings = new List<BundleSetting>();

        AddFilesInFolder("Local", "Assets/TestRemoteResources/Local", true, true, true, bundleSettings);
        AddFilesInFolder("Object", "Assets/TestRemoteResources/Object", false, true, true, bundleSettings);
        AddFilesInFolder("Object_RootOnly", "Assets/TestRemoteResources/Object_RootOnly", false, false, true, bundleSettings);
        AddFilesInFolder("Scene", "Assets/TestRemoteResources/Scene", false, true, true, bundleSettings);

        AddFilesInFolder("Ref_A", "Assets/TestRemoteResources/Ref_A", false, true, true, bundleSettings);
        AddFilesInFolder("Ref_B", "Assets/TestRemoteResources/Ref_B", false, true, true, bundleSettings);
        AddFilesInFolder("Ref_Shared", "Assets/TestRemoteResources/Ref_Shared", false, true, true, bundleSettings);
        AddFilesInFolder("Ref2_A", "Assets/TestRemoteResources/Ref2_A", false, true, true, bundleSettings);
        AddFilesInFolder("Ref2_B", "Assets/TestRemoteResources/Ref2_B", false, true, true, bundleSettings);
        AddFilesInFolder("Ref2_Shared", "Assets/TestRemoteResources/Ref2_Shared", false, true, true, bundleSettings);

        return bundleSettings;
    }

    static void AddFilesInFolder(string bundleName, string folderPath, bool local, bool includeSubfolder, bool compress, List<BundleSetting> targetList)
    {
        var assetPath = new List<string>();
        var loadPath = new List<string>();

        Utility.GetFilesInDirectory(assetPath, loadPath, folderPath, includeSubfolder);

        targetList.Add(new BundleSetting()
        {
            AssetNames = assetPath.ToArray(),
            AddressableNames = loadPath.ToArray(),
            BundleName = bundleName,
            AutoSharedBundle = true,
            CompressBundle = compress,
            IncludedInPlayer = local
        });
    }

    public override bool IsValid() => true;
}