using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace BundleSystem
{
    /// <summary>
    /// Pre/Post build processors that includes local bundle and manifest into output build.
    /// This is automtically called by unity.
    /// </summary>
    public class AssetBundleBuildProcessors : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        /// <summary>
        /// callback order. if you want to do something before copy local bundles into streaming folder.
        /// make sure you have less callback order than this.
        /// </summary>
        public int callbackOrder => 999;

        /// <summary>
        /// Preprocess function, copy local bundles and manifest into streaming folder.
        /// </summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (!AssetBundleBuildSetting.TryGetActiveSetting(out var setting)) return;
            if (Directory.Exists(BundleManager.LocalBundleRuntimePath)) Directory.Delete(BundleManager.LocalBundleRuntimePath, true);
            if (!Directory.Exists(Application.streamingAssetsPath)) Directory.CreateDirectory(Application.streamingAssetsPath);
            
            if (!Directory.Exists(setting.OutputPath))
            {
                Debug.LogError("Missing built bundle directory, Locus bundle system won't work properly.");
                return;
            }

            //load manifest and make local bundle list
            var manifest = JsonUtility.FromJson<AssetBundleBuildManifest>(File.ReadAllText(Utility.CombinePath(setting.OutputPath, BundleManager.ManifestFileName)));
            var localBundleNames = manifest.BundleInfos.Where(bi => bi.IsLocal).Select(bi => bi.BundleName).ToList();

            Directory.CreateDirectory(BundleManager.LocalBundleRuntimePath);

            //copy only manifest and local bundles                        
            foreach (var file in new DirectoryInfo(setting.OutputPath).GetFiles())
            {
                if (!localBundleNames.Contains(file.Name) && BundleManager.ManifestFileName != file.Name) continue;
                FileUtil.CopyFileOrDirectory(file.FullName, Utility.CombinePath(BundleManager.LocalBundleRuntimePath, file.Name));
            }

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Postprocess function. removes local bundles folder after a build completes.
        /// </summary>
        /// <param name="report"></param>
        public void OnPostprocessBuild(BuildReport report)
        {
            //delete directory and meta file
            if (FileUtil.DeleteFileOrDirectory(BundleManager.LocalBundleRuntimePath) ||
                FileUtil.DeleteFileOrDirectory(BundleManager.LocalBundleRuntimePath + ".meta"))
            {
                AssetDatabase.Refresh();
            }
        }
    }
}
