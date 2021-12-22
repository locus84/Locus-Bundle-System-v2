using System.Collections.Generic;
using UnityEngine;

namespace BundleSystem
{
    public abstract class AssetBundleBuildSetting : ScriptableObject
    {
#if UNITY_EDITOR
        static AssetBundleBuildSetting s_ActiveSetting = null;
        static bool isDirty = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void EditorRuntimeInitialize()
        {
            RebuildEditorAssetDatabaseMap();
        }

        class DirtyChecker : UnityEditor.AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
            {
                //does not matter, we just need to rebuild editordatabase later on
                AssetBundleBuildSetting.isDirty = true;
            }
        }

        /// <summary>
        /// Rebuild EditorAssetDatabaseMap from current active AssetBundleBuildSetting 
        /// </summary>
        public static void RebuildEditorAssetDatabaseMap()
        {
            if(AssetBundleBuildSetting.TryGetActiveSetting(out var setting)) 
            {
                BundleManager.SetEditorDatabase(setting.CreateEditorDatabase());
                isDirty = false;
            }
        }
        
        
        /// <summary>
        /// Build EditorAssetDatabaseMap from this AssetBundleBuildSetting.
        /// To emulate Assetbundle functioanlies in editor.
        /// </summary>
        public EditorDatabaseMap CreateEditorDatabase()
        {
            var setting = new EditorDatabaseMap();
            setting.UseAssetDatabase = !EmulateInEditor || !Application.isPlaying;
            setting.CleanCache = CleanCacheInEditor;
            setting.UseOuputAsRemote = EmulateWithoutRemoteURL;
            setting.OutputPath = Utility.CombinePath(OutputPath, UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString());

            var bundleSettings = GetBundleSettings();
            for (int i = 0; i < bundleSettings.Count; i++)
            {
                var currentSetting = bundleSettings[i]; 
                setting.Append(currentSetting.BundleName, currentSetting.AssetNames, currentSetting.AddressableNames);
            }
            return setting;
        }

        /// <summary>
        /// Try to find bundle settings that you set active using context menu.
        /// </summary>
        /// <param name="setting">found setting</param>
        /// <param name="findIfNotExist">if none of them is active, set the first found as active setting.</param>
        /// <returns>returns true if found, false otherwise</returns>
        public static bool TryGetActiveSetting(out AssetBundleBuildSetting setting, bool findIfNotExist = true)
        {
            if (s_ActiveSetting != null) 
            {
                setting = s_ActiveSetting;
                return true;
            }

            var defaultGUID = UnityEditor.EditorPrefs.GetString("LocusActiveBundleSetting", string.Empty);
            var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(defaultGUID);

            if (!string.IsNullOrEmpty(assetPath))
            {
                var found = UnityEditor.AssetDatabase.LoadAssetAtPath<AssetBundleBuildSetting>(assetPath);
                if(found != null)
                {
                    s_ActiveSetting = found;
                    setting = s_ActiveSetting;
                    return true;
                }
            }
            
            if(!findIfNotExist)
            {
                setting = default;
                return false;
            }

            var typeName = typeof(AssetBundleBuildSetting).Name;
            var assetPathes = UnityEditor.AssetDatabase.FindAssets($"t:{typeName}");

            if (assetPathes.Length == 0) 
            {
                setting = default;
                return false;
            }

            var guid = UnityEditor.AssetDatabase.GUIDToAssetPath(UnityEditor.AssetDatabase.GUIDToAssetPath(assetPathes[0]));
            UnityEditor.EditorPrefs.GetString("LocusActiveBundleSetting", guid);
            s_ActiveSetting = UnityEditor.AssetDatabase.LoadAssetAtPath<AssetBundleBuildSetting>(UnityEditor.AssetDatabase.GUIDToAssetPath(assetPathes[0]));

            setting = s_ActiveSetting;
            return true;
        }

        /// <summary>
        /// Set provided setting as an active one.
        /// To use in playmode and building.
        /// </summary>
        /// <param name="setting">settings to set active</param>
        /// <param name="rebuildDatabaseMap">rebuild EditorAssetDatabaseMap right away?</param>
        public static void SetActiveSetting(AssetBundleBuildSetting setting, bool rebuildDatabaseMap = false)
        {
            var assetPath = UnityEditor.AssetDatabase.GetAssetPath(setting);
            UnityEditor.EditorPrefs.SetString("LocusActiveBundleSetting", UnityEditor.AssetDatabase.AssetPathToGUID(assetPath));
            s_ActiveSetting = setting;

            //rebuild map right away
            if(rebuildDatabaseMap) BundleManager.SetEditorDatabase(setting.CreateEditorDatabase());
            //if not, make it dirty
            isDirty = !rebuildDatabaseMap;
        }

        /// <summary>
        /// Output path of the built AssetBundles.
        /// </summary>
        public string OutputPath => Application.dataPath.Remove(Application.dataPath.Length - 6) + OutputFolder;

        /// <summary>
        /// Output path of the built Local AssetBundles.
        /// </summary>
        public string LocalOutputPath => Application.dataPath.Remove(Application.dataPath.Length - 6) + $"{OutputFolder}/Local";
#endif

        /// <summary>
        /// output folder path relatvie to project root directory
        /// </summary>
        [SerializeField]
        [Tooltip("AssetBundle build output folder")]
        public string OutputFolder = "AssetBundles";

        /// <summary>
        /// Remote URL to download AssetBundles
        /// </summary>
        [Tooltip("Remote URL for downloading remote bundles")]
        public string RemoteURL = "http://localhost/";

        /// <summary>
        /// Use assetbundles in editor. if false, it'll use EditorAssetDatabaseMap.
        /// </summary>
        [Tooltip("Use built asset bundles even in editor")]
        public bool EmulateInEditor = false;

        /// <summary>
        /// Use Remote output folder when emulating remote bundles
        /// Useful when your contents delevery network is not yet ready but you want to test with actual AssetBundles.
        /// </summary>
        [Tooltip("Use Remote output folder when emulating remote bundles")]
        public bool EmulateWithoutRemoteURL = false;

        /// <summary>
        /// Clean up player cache when initializing for testing purpose.
        /// </summary>
        [Tooltip("Clean cache when initializing BundleManager for testing purpose")]
        public bool CleanCacheInEditor = false;

        /// <summary>
        /// Build Local bundles when building player.
        /// </summary>
        [Tooltip("Skip build local bundles when building bundles")]
        public bool SkipBuildLocalBundles = false;

        /// <summary>
        /// Force rebuild. ignore cache. if something wrong with built bundles. Try again with this paramteter set true.
        /// </summary>
        public bool ForceRebuild = false;

        /// <summary>
        /// Provide actual infomations about each bundle.
        /// </summary>
        public abstract List<BundleSetting> GetBundleSettings();

        /// <summary>
        /// Check setting is valid. when you need to say the setting is not valid and want to prevent AssetBundle building.
        /// </summary>
        public virtual bool IsValid() => true;
    }


    [System.Serializable]
    public struct BundleSetting
    {
        /// <summary>
        /// Name of the bundle.
        /// </summary>
        public string BundleName;
        /// <summary>
        /// Whether this AssetBundle should be included in build when building player or not.
        /// </summary>
        public bool IncludedInPlayer;
        /// <summary>
        /// Pathes of Assets that's included in this bundle.
        /// </summary>
        public string[] AssetNames;
        /// <summary>
        /// Additional Asset name that you can use it when loading from the AssetBundle.
        /// </summary>
        public string[] AddressableNames;
        /// <summary>
        /// Whether this AssetBundle should be compressed or not.
        /// </summary>
        public bool CompressBundle;
        /// <summary>
        /// Whether this bundle is associated with shared bundle generation or not. 
        /// </summary>
        public bool AutoSharedBundle;
    }
}

     