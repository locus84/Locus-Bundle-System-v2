using System.Collections.Generic;
using UnityEngine;

namespace BundleSystem
{
    /// <summary>
    /// Sample assetbundle label based build settings, it always mark bundles Compress, and Autoshared, 
    /// if bundle name contains 'local'(ignore case), then it'll be treated as local bundle
    /// </summary>
    [CreateAssetMenu(fileName = "LabelBasedAssetBundleBuildSetting.asset", menuName = "Create Label Based AssetBundle Build Setting", order = 999)]
    public class LabelBasedAssetBundleBuildSettings : AssetBundleBuildSetting
    {
        public override List<BundleSetting> GetBundleSettings()
        {
            var result = new List<BundleSetting>();
            var builds = UnityEditor.Build.Content.ContentBuildInterface.GenerateAssetBundleBuilds();
            foreach(var abBuild in builds)
            {
                result.Add(new BundleSetting()
                {
                    BundleName = abBuild.assetBundleName,
                    AddressableNames = abBuild.addressableNames,
                    AssetNames = abBuild.assetNames,
                    AutoSharedBundle = true,
                    CompressBundle = true,
                    IncludedInPlayer = abBuild.assetBundleName.IndexOf("local", System.StringComparison.OrdinalIgnoreCase) >= 0
                });
            }
            return result;
        }
    }
}
