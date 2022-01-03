using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace BundleSystem
{
    internal class ReloadGroup
    {
        public int Index { get; private set; }
        public ReloadGroup(int index) => Index = index;
        public List<LoadedBundle> Bundles = new List<LoadedBundle>();
        public int ReferenceCount;
        public bool IsDirty = false;
        public bool IsInvalid = false;
    }

    public partial class BundleManager
    {
        private static Dictionary<string, ReloadGroup> s_ReloadGroupDict = new Dictionary<string, ReloadGroup>();

        private static void RefreshReloadGroup(AssetBundleBuildManifest manifest)
        {
            foreach(var kv in s_ReloadGroupDict) kv.Value.IsInvalid = true;
                
            s_ReloadGroupDict = BundleReloadGroup(manifest);
            foreach(var kv in s_AssetBundles)
            {
                var group = s_ReloadGroupDict[kv.Key];
                group.ReferenceCount += kv.Value.ReferenceCount;
                kv.Value.Group = group;
                group.Bundles.Add(kv.Value);
            }

            //apply dirty
            foreach(var kv in s_ReloadGroupDict)
            {
                if(kv.Value.ReferenceCount > 0) kv.Value.IsDirty = true;
            }
        }

        private static Dictionary<string, ReloadGroup> BundleReloadGroup(AssetBundleBuildManifest manifest)
        {
            var result = new Dictionary<string, ReloadGroup>();
            var index = 0;
            var list = new List<string>();
            foreach (var bi in manifest.BundleInfos)
            {
                list.Clear();
                var foundGroup = default(ReloadGroup);

                list.Add(bi.BundleName);
                if (result.TryGetValue(bi.BundleName, out var group))
                {
                    foundGroup = group;
                }

                foreach (var dep in bi.Dependencies)
                {
                    if (foundGroup == null && result.TryGetValue(dep, out var depGroup))
                    {
                        foundGroup = depGroup;
                    }
                    list.Add(bi.BundleName);
                }

                if (foundGroup == null) foundGroup = new ReloadGroup(++index);

                foreach (var name in list)
                {
                    result[name] = foundGroup;
                }
            }

            return result;
        }

    }
    
}