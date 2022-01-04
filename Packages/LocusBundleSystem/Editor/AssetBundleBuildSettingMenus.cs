using System.IO;
using BundleSystem;
using UnityEditor;

public static class AssetBundleBuildSettingMenus
{
    [MenuItem("CONTEXT/AssetBundleBuildSetting/Set As Active Setting")]
    static void SetDefaultSetting(MenuCommand command)
    {
        var setting = (AssetBundleBuildSetting)command.context;
        AssetBundleBuildSetting.SetActiveSetting(setting);
    }

    [MenuItem("CONTEXT/AssetBundleBuildSetting/Build With This Setting")]
    static void BuildThisSetting(MenuCommand command)
    {
        var setting = (AssetBundleBuildSetting)command.context;
        AssetBundleBuilder.BuildAssetBundles(setting);
    }

    [MenuItem("CONTEXT/AssetBundleBuildSetting/Get Expected Shared Bundles")]
    static void GetSharedBundleLog(MenuCommand command)
    {
        var setting = (AssetBundleBuildSetting)command.context;
        AssetBundleBuilder.WriteExpectedSharedBundles(setting);
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
