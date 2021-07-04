using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace BundleSystem
{
    /// <summary>
    /// AssetBundle Manifest class.
    /// This class contains complete set of bundles that's needed.
    /// like Catalog in Addressables.
    /// </summary>
    [System.Serializable]
    public class AssetBundleBuildManifest
    {

        /// <summary>
        /// Parse previousely applied AssetBundleBuildManifest.
        /// Which is stored in PlayerPref with "CachedManifest" key
        /// </summary>
        /// <returns>returns true if found, false otherwise</returns>
        public static bool TryGetCachedManifest(out AssetBundleBuildManifest manifest)
        {
            return AssetBundleBuildManifest.TryParse(PlayerPrefs.GetString("CachedManifest", string.Empty), out manifest);
        }

        /// <summary>
        /// Parse Json string to AssetBundleBuildManifest instance.
        /// Used when fetching latest manifest of loading cached manifest.
        /// </summary>
        /// <param name="json">input json string</param>
        /// <param name="manifest">manifest instance deserialized</param>
        /// <returns>returns true if succeeded, false otherwise</returns>
        public static bool TryParse(string json, out AssetBundleBuildManifest manifest)
        {
            if (string.IsNullOrEmpty(json))
            {
                manifest = default;
                return false;
            }

            try
            {
                manifest = JsonUtility.FromJson<AssetBundleBuildManifest>(json);
                return true;
            }
            catch
            {
                manifest = default;
                return false;
            }
        }

        /// <summary>
        /// Represents an Assetbundle.
        /// Includes necessary informations.
        /// </summary>
        [System.Serializable]
        public struct BundleInfo
        {
            /// <summary>
            /// AssetBundle's name
            /// </summary>
            public string BundleName;

            /// <summary>
            /// Is this bundle included is build?
            /// Declaimer : This does not mean actually the bundle is included in player.
            /// </summary>
            public bool IsLocal;

            /// <summary>
            /// Hash string used when checking two bundle is same.
            /// </summary>
            public string HashString;
            
            /// <summary>
            /// The bundle names that this bundle depends on.
            /// </summary>
            public List<string> Dependencies;

            /// <summary>
            /// Assetbundle Size in bytes
            /// </summary>
            public long Size;

            /// <summary>
            /// Convert to unity's CachedAssetBundle struct to check the bundle is cached.
            /// </summary>
            public CachedAssetBundle ToCachedBundle() => new CachedAssetBundle(BundleName, Hash128.Parse(HashString));
        }

        /// <summary>
        /// Bundle infmation list.
        /// </summary>
        /// <typeparam name="BundleInfo"></typeparam>
        /// <returns></returns>
        public List<BundleInfo> BundleInfos = new List<BundleInfo>();

        /// <summary>
        /// Build Target string, used when building remote path string.
        /// </summary>
        public string BuildTarget;

        /// <summary>
        /// This does not included in hash calculation, used to find newer version between cached manifest and local manifest
        /// </summary>
        public long BuildTime;

        /// <summary>
        /// Remote Base URL. Files will be lied in RemoteURL/BuildTarget.
        /// </summary>
        public string RemoteURL;

        /// <summary>
        /// Hash String to check this manifest is same with another or not.
        /// </summary>
        public string GlobalHashString;
        
        /// <summary>
        /// Find BundleInfo of an AssetBundle from this Manifest.
        /// </summary>
        /// <param name="name">Name of AssetBundle</param>
        /// <param name="info">Found BundleInfo</param>
        /// <returns>returns true if found, false otherwise</returns>
        public bool TryGetBundleInfo(string name, out BundleInfo info)
        {
            var index = BundleInfos.FindIndex(bundleInfo => bundleInfo.BundleName == name);
            info = index >= 0 ? BundleInfos[index] : default;
            return index >= 0;
        }

        public bool TryGetBundleHash(string name, out Hash128 hash)
        {
            if (TryGetBundleInfo(name, out var info))
            {
                hash = Hash128.Parse(info.HashString);
                return true;
            }
            else
            {
                hash = default;
                return false;
            }
        }
        
        /// <summary>
        /// This collects BundleInfos that's provided names, and their dependencies.
        /// When you need to download only part of bundles from this manifest. 
        /// </summary>
        /// <param name="subsetNames">Bundle names you interested</param>
        /// <returns>BundleInfos you requested and their dependencies</returns>
        public List<BundleInfo> CollectSubsetBundleInfoes(IEnumerable<string> subsetNames)
        {
            var bundleInfoDic = BundleInfos.ToDictionary(info => info.BundleName);
            var resultDic = new Dictionary<string, BundleInfo>();
            foreach (var name in subsetNames)
            {
                if (!bundleInfoDic.TryGetValue(name, out var bundle))
                {
                    if (BundleManager.LogMessages) Debug.LogWarning($"Name you provided ({name}) could not be found");
                    continue;
                }

                if (!resultDic.ContainsKey(bundle.BundleName))
                {
                    resultDic.Add(bundle.BundleName, bundle);
                }

                for (int i = 0; i < bundle.Dependencies.Count; i++)
                {
                    var depName = bundle.Dependencies[i];
                    if (!resultDic.ContainsKey(depName)) resultDic.Add(depName, bundleInfoDic[depName]);
                }
            }

            return resultDic.Values.ToList();
        }
    }
}
