﻿using System.Collections.Generic;
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
        AddFilesInFolder("Object", "Assets/TestRemoteResources/Object", true, true, false, bundleSettings);
        AddFilesInFolder("Object_RootOnly", "Assets/TestRemoteResources/Object_RootOnly", true, false, true, bundleSettings);
        AddFilesInFolder("Scene", "Assets/TestRemoteResources/Scene", true, true, false, bundleSettings);

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