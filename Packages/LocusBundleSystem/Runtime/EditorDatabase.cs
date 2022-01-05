using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
namespace BundleSystem
{
    /// <summary>
    /// This class is for emulating AssetBundles without actaully using it.
    /// All the functions/properties are used by the framework only.
    /// </summary>
    public class EditorDatabaseMap
    {        
        internal bool UseAssetDatabase;
        internal bool CleanCache;
        internal bool UseOuputAsRemote;
        internal string OutputPath;
        internal string UserVersionString;

        private Dictionary<string, Dictionary<string, List<string>>> m_Map = new Dictionary<string, Dictionary<string, List<string>>>();
        private Dictionary<string, string> m_ScenePathToBundleName = new Dictionary<string, string>();
        static List<string> s_EmptyStringList = new List<string>();

        internal IEnumerable<string> GetBundleNames() => m_Map.Keys;

        internal Dictionary<string, string> GetScenePathToBundleName() => new Dictionary<string, string>(m_ScenePathToBundleName);
        
        internal void Append(string bundleName, string[] assetNames, string[] addressableNames)
        {
            var assetDict = new Dictionary<string, List<string>>();
            for(int i = 0; i < assetNames.Length; i++)
            {
                var loadName = addressableNames[i];
                var assetPath = assetNames[i];

                if(!assetDict.TryGetValue(loadName, out var list))
                {
                    list = new List<string>();
                    assetDict.Add(loadName, list);
                }
                list.Add(assetPath);
                
                if(assetPath.EndsWith(".unity"))
                {
                    m_ScenePathToBundleName.Add(assetPath, bundleName);
                }
            }
            m_Map.Add(bundleName, assetDict);
        }

        internal List<string> GetAssetPaths(string bundleName, string assetName)
        {
            if (!m_Map.TryGetValue(bundleName, out var innerDic)) return s_EmptyStringList;
            if (!innerDic.TryGetValue(assetName, out var pathList)) return s_EmptyStringList;
            return pathList;
        }

        internal List<string> GetAssetPaths(string bundleName)
        {
            if (!m_Map.TryGetValue(bundleName, out var innerDic)) return s_EmptyStringList;
            return innerDic.Values.SelectMany(list => list).ToList();
        }

        internal bool IsAssetExist(string bundleName, string assetName)
        {
            var assets = GetAssetPaths(bundleName, assetName);
            return assets.Count > 0;
        }

        internal string GetAssetPath<T>(string bundleName, string assetName) where T : UnityEngine.Object
        {
            var assets = GetAssetPaths(bundleName, assetName);
            if (assets.Count == 0) return string.Empty;

            var typeExpected = typeof(T);
            var foundIndex = 0;

            for (int i = 0; i < assets.Count; i++)
            {
                var foundType = UnityEditor.AssetDatabase.GetMainAssetTypeAtPath(assets[i]);
                if (foundType == typeExpected || foundType.IsSubclassOf(typeExpected))
                {
                    foundIndex = i;
                    break;
                }
            }

            return assets[foundIndex];
        }
    }
}
#endif