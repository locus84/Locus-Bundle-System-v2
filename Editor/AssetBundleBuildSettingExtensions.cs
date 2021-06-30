﻿using System.IO;
using BundleSystem;
using UnityEditor;

public static class AssetBundleBuildSettingMenus
{
    [MenuItem("CONTEXT/AssetBundleBuildSetting/Set As Active Setting")]
    static void SetDefaultSetting()
    {
        var setting = Selection.activeObject as AssetBundleBuildSetting;
        if (setting != null) AssetBundleBuildSetting.SetActiveSetting(setting);
    }

    [MenuItem("CONTEXT/AssetBundleBuildSetting/Build With This Setting")]
    static void BuildThisSetting()
    {
        var setting = Selection.activeObject as AssetBundleBuildSetting;
        if (setting != null) AssetBundleBuilder.BuildAssetBundles(setting);
    }

    [MenuItem("CONTEXT/AssetBundleBuildSetting/Get Expected Shared Bundles")]
    static void GetSharedBundleLog()
    {
        var setting = Selection.activeObject as AssetBundleBuildSetting;
        if (setting != null) AssetBundleBuilder.WriteExpectedSharedBundles(setting);
    }

    // Add menu named "My Window" to the Window menu
    [MenuItem("Window/Asset Management/Select Active AssetBundle Build Setting")]
    static void SelectActiveSettings()
    {
        if (AssetBundleBuildSetting.TryGetActiveSetting(out var setting))
        {
            Selection.activeObject = setting;
        }
        else
        {
            EditorUtility.DisplayDialog("Warning", "No Setting Found", "Okay");
        }
    }

}
