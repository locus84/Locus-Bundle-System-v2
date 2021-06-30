using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using System.Linq;

namespace BundleSystem
{
    /// <summary>
    /// Handle Resources expecially assetbundles.
    /// Also works in editor
    /// </summary>
    public static partial class BundleManager
    {
        //instance is almost only for coroutines
        internal static int UnityMainThreadId { get; private set; }
        private static BundleManagerHelper s_Helper { get; set; }
        public const string ManifestFileName = "Manifest.json";
        public static string LocalBundleRuntimePath => Utility.CombinePath(Application.streamingAssetsPath, "localbundles");

        //Asset bundles that is loaded keep it static so we can easily call this in static method
        private static Dictionary<string, LoadedBundle> s_AssetBundles = new Dictionary<string, LoadedBundle>();
        private static Dictionary<string, Hash128> s_LocalBundles = new Dictionary<string, Hash128>();
        private static Dictionary<string, SceneInfo> s_SceneInfos = new Dictionary<string, SceneInfo>();


#if UNITY_EDITOR
        public static bool UseAssetDatabaseMap { get; private set; } = true;
        public static void SetEditorDatabase(EditorDatabaseMap map)
        {
            s_EditorDatabaseMap = map;
            //here we fill assetbundleDictionary into fake bundles
            if (s_EditorDatabaseMap.UseAssetDatabase)
            {
                s_AssetBundles = s_EditorDatabaseMap.GetBundleNames().ToDictionary(name => name, name => new LoadedBundle(name));
                s_SceneInfos.Clear();
                foreach(var kv in s_EditorDatabaseMap.GetScenePathToBundleName())
                {
                    var info = new SceneInfo 
                    {
                        Name = System.IO.Path.GetFileNameWithoutExtension(kv.Key),
                        Path = kv.Key,
                        LoadedBundle = new LoadedBundle(kv.Value),
                    };
                    s_SceneInfos[info.Name] = info;
                    s_SceneInfos[info.Path] = info;
                }
            }
        }
        private static EditorDatabaseMap s_EditorDatabaseMap;
        private static void EnsureAssetDatabase()
        {
            if (!Application.isPlaying && s_EditorDatabaseMap == null)
            {
                throw new System.Exception("EditorDatabase is null, try call SetEditorDatabase before calling actual api in non-play mode");
            }
        }
#endif

        public static bool Initialized { get; private set; } = false;
        public static string LocalURL { get; private set; }
        public static string RemoteURL { get; private set; }
        public static string GlobalBundleHash { get; private set; }
        public static bool LogMessages { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Setup()
        {
            UnityMainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            //only unload callback is necessary
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
            var managerGo = new GameObject("_BundleManager");
            GameObject.DontDestroyOnLoad(managerGo);
            s_Helper = managerGo.AddComponent<BundleManagerHelper>();
        }

        static void CollectSceneNames(LoadedBundle loadedBundle)
        {
            var scenePathes = loadedBundle.Bundle.GetAllScenePaths();
            foreach (var path in scenePathes)
            {
                var info = new SceneInfo{
                    Name = System.IO.Path.GetFileNameWithoutExtension(path),
                    Path = path,
                    LoadedBundle = loadedBundle
                };
                s_SceneInfos[info.Name] = info;
                s_SceneInfos[info.Path] = info;
            }
        }

        private static void OnDestroy()
        {
            foreach (var kv in s_AssetBundles)
                kv.Value.Dispose();
            s_AssetBundles.Clear();
        }

        /// <summary>
        /// Initialize bundle system and load local bundles
        /// </summary>
        /// <param name="altRemoteUrl">alternative remote url, local manifest's RemoteUrl field will be used as default</param>
        /// <returns>async operation that can be yield return</returns>
        public static BundleAsyncOperation Initialize(string altRemoteUrl = null)
        {
            var result = new BundleAsyncOperation();
            s_Helper.StartCoroutine(CoInitalizeLocalBundles(result, altRemoteUrl));
            return result;
        }

        static IEnumerator CoInitalizeLocalBundles(BundleAsyncOperation result, string altRemoteUrl)
        {
            if (Initialized)
            {
                result.Done(BundleErrorCode.Success);
                yield break;
            }

#if UNITY_EDITOR
            if (s_EditorDatabaseMap.UseAssetDatabase)
            {
                UseAssetDatabaseMap = true;
                Initialized = true;
                result.Done(BundleErrorCode.Success);
                yield break; //use asset database
            }

            //now use actual bundle
            UseAssetDatabaseMap = false;

            //cache control
            if (s_EditorDatabaseMap.CleanCache) Caching.ClearCache();

            LocalURL = s_EditorDatabaseMap.OutputPath;
#else
            LocalURL = LocalBundleRuntimePath;
#endif

            //platform specific path setting
            if (Application.platform != RuntimePlatform.Android &&
                Application.platform != RuntimePlatform.WebGLPlayer)
            {
                LocalURL = "file://" + LocalURL;
            }

            if (LogMessages) Debug.Log($"LocalURL : {LocalURL}");

            foreach (var kv in s_AssetBundles)
                kv.Value.Dispose();

            s_SceneInfos.Clear();
            s_AssetBundles.Clear();
            s_LocalBundles.Clear();

            var manifestReq = UnityWebRequest.Get(Utility.CombinePath(LocalURL, ManifestFileName));
            yield return manifestReq.SendWebRequest();
            if (manifestReq.isHttpError || manifestReq.isNetworkError)
            {
                result.Done(BundleErrorCode.NetworkError);
                yield break;
            }

            if (!AssetBundleBuildManifest.TryParse(manifestReq.downloadHandler.text, out var localManifest))
            {
                result.Done(BundleErrorCode.ManifestParseError);
                yield break;
            }

            //cached version is recent one.
            var cacheIsValid = AssetBundleBuildManifest.TryParse(PlayerPrefs.GetString("CachedManifest", string.Empty), out var cachedManifest)
                && cachedManifest.BuildTime > localManifest.BuildTime;

            var localBundleInfos = localManifest.BundleInfos.Where(bi => bi.IsLocal).ToArray();
            result.SetIndexLength(localBundleInfos.Length);
            for (int i = 0; i < localBundleInfos.Length; i++)
            {
                result.SetCurrentIndex(i);
                result.SetCachedBundle(true);
                AssetBundleBuildManifest.BundleInfo bundleInfoToLoad;
                AssetBundleBuildManifest.BundleInfo cachedBundleInfo = default;
                var localBundleInfo = localBundleInfos[i];

                bool useLocalBundle =
                    !cacheIsValid || //cache is not valid or...
                    !cachedManifest.TryGetBundleInfo(localBundleInfo.BundleName, out cachedBundleInfo) || //missing bundle or... 
                    !Caching.IsVersionCached(cachedBundleInfo.ToCachedBundle()); //is not cached no unusable.

                bundleInfoToLoad = useLocalBundle ? localBundleInfo : cachedBundleInfo;
                var loadPath = Utility.CombinePath(LocalURL, bundleInfoToLoad.BundleName);

                var bundleReq = UnityWebRequestAssetBundle.GetAssetBundle(loadPath, Hash128.Parse(bundleInfoToLoad.HashString));
                var bundleOp = bundleReq.SendWebRequest();
                while (!bundleOp.isDone)
                {
                    result.SetProgress(bundleOp.progress);
                    yield return null;
                }

                if (!bundleReq.isHttpError && !bundleReq.isNetworkError)
                {
                    var loadedBundle = new LoadedBundle(bundleInfoToLoad, loadPath, DownloadHandlerAssetBundle.GetContent(bundleReq), useLocalBundle);
                    s_AssetBundles.Add(localBundleInfo.BundleName, loadedBundle);
                    CollectSceneNames(loadedBundle);

                    if (LogMessages) Debug.Log($"Local bundle Loaded - Name : { localBundleInfo.BundleName }, Hash : { bundleInfoToLoad.HashString }");
                }
                else
                {
                    result.Done(BundleErrorCode.NetworkError);
                    yield break;
                }

                bundleReq.Dispose();
                s_LocalBundles.Add(localBundleInfo.BundleName, Hash128.Parse(localBundleInfo.HashString));
            }

            var remoteUrl = string.IsNullOrEmpty(altRemoteUrl) ? localManifest.RemoteURL : altRemoteUrl;
            RemoteURL = Utility.CombinePath(remoteUrl, localManifest.BuildTarget);
#if UNITY_EDITOR
            if (s_EditorDatabaseMap.UseOuputAsRemote)
                RemoteURL = "file://" + s_EditorDatabaseMap.OutputPath;
#endif
            Initialized = true;
            if (LogMessages) Debug.Log($"Initialize Success \nRemote URL : {RemoteURL} \nLocal URL : {LocalURL}");
            result.Done(BundleErrorCode.Success);
        }

        /// <summary>
        /// get last cached manifest, to support offline play
        /// </summary>
        /// <returns>returns true if found, false otherwise</returns>
        public static bool TryGetCachedManifest(out AssetBundleBuildManifest manifest)
        {
            return AssetBundleBuildManifest.TryParse(PlayerPrefs.GetString("CachedManifest", string.Empty), out manifest);
        }

        public static BundleAsyncOperation<AssetBundleBuildManifest> GetManifest()
        {
            var result = new BundleAsyncOperation<AssetBundleBuildManifest>();
            s_Helper.StartCoroutine(CoGetManifest(result));
            return result;
        }

        static IEnumerator CoGetManifest(BundleAsyncOperation<AssetBundleBuildManifest> result)
        {
            if (!Initialized)
            {
                Debug.LogError("Do Initialize first");
                result.Done(BundleErrorCode.NotInitialized);
                yield break;
            }

#if UNITY_EDITOR
            if (UseAssetDatabaseMap)
            {
                result.Result = new AssetBundleBuildManifest();
                result.Done(BundleErrorCode.Success);
                yield break;
            }
#endif

            var manifestReq = UnityWebRequest.Get(Utility.CombinePath(RemoteURL, ManifestFileName));
            yield return manifestReq.SendWebRequest();

            if (manifestReq.isHttpError || manifestReq.isNetworkError)
            {
                result.Done(BundleErrorCode.NetworkError);
                yield break;
            }

            var remoteManifestJson = manifestReq.downloadHandler.text;
            manifestReq.Dispose();

            if (!AssetBundleBuildManifest.TryParse(remoteManifestJson, out var remoteManifest))
            {
                result.Done(BundleErrorCode.ManifestParseError);
                yield break;
            }

            result.Result = remoteManifest;
            result.Done(BundleErrorCode.Success);
        }

        /// <summary>
        /// Get download size of entire bundles(except cached)
        /// </summary>
        /// <param name="manifest">manifest you get from GetManifest() function</param>
        /// <param name="subsetNames">names that you interested among full bundle list(optional)</param>
        /// <returns></returns>
        public static long GetDownloadSize(AssetBundleBuildManifest manifest, IEnumerable<string> subsetNames = null)
        {
            if (!Initialized)
            {
                throw new System.Exception("BundleManager is not initialized");
            }

            long totalSize = 0;

            var bundleInfoList = subsetNames == null ? manifest.BundleInfos : manifest.CollectSubsetBundleInfoes(subsetNames);

            for (int i = 0; i < bundleInfoList.Count; i++)
            {
                var bundleInfo = bundleInfoList[i];
                var uselocalBundle = s_LocalBundles.TryGetValue(bundleInfo.BundleName, out var localHash) && localHash == Hash128.Parse(bundleInfo.HashString);
                if (!uselocalBundle && !Caching.IsVersionCached(bundleInfo.ToCachedBundle()))
                    totalSize += bundleInfo.Size;
            }

            return totalSize;
        }


        /// <summary>
        /// acutally download assetbundles load from cache if cached 
        /// </summary>
        /// <param name="manifest">manifest you get from GetManifest() function</param>
        /// <param name="subsetNames">names that you interested among full bundle list(optional)</param>
        public static BundleAsyncOperation<bool> DownloadAssetBundles(AssetBundleBuildManifest manifest, IEnumerable<string> subsetNames = null)
        {
            var result = new BundleAsyncOperation<bool>();
            s_Helper.StartCoroutine(CoDownloadAssetBundles(manifest, subsetNames, result));
            return result;
        }

        static IEnumerator CoDownloadAssetBundles(AssetBundleBuildManifest manifest, IEnumerable<string> subsetNames, BundleAsyncOperation<bool> result)
        {
            if (!Initialized)
            {
                Debug.LogError("Do Initialize first");
                result.Done(BundleErrorCode.NotInitialized);
                yield break;
            }

#if UNITY_EDITOR
            if (UseAssetDatabaseMap)
            {
                result.Done(BundleErrorCode.Success);
                yield break;
            }
#endif

            var bundlesToUnload = new HashSet<string>(s_AssetBundles.Keys);
            var downloadBundleList = subsetNames == null ? manifest.BundleInfos : manifest.CollectSubsetBundleInfoes(subsetNames);
            var bundleReplaced = false; //bundle has been replaced

            result.SetIndexLength(downloadBundleList.Count);

            for (int i = 0; i < downloadBundleList.Count; i++)
            {
                result.SetCurrentIndex(i);
                var bundleInfo = downloadBundleList[i];
                var bundleHash = Hash128.Parse(bundleInfo.HashString);

                //remove from the set so we can track bundles that should be cleared
                bundlesToUnload.Remove(bundleInfo.BundleName);

                var islocalBundle = s_LocalBundles.TryGetValue(bundleInfo.BundleName, out var localHash) && localHash == bundleHash;
                var isCached = Caching.IsVersionCached(bundleInfo.ToCachedBundle());
                result.SetCachedBundle(isCached);

                var loadURL = islocalBundle ? Utility.CombinePath(LocalURL, bundleInfo.BundleName) : Utility.CombinePath(RemoteURL, bundleInfo.BundleName);
                if (LogMessages) Debug.Log($"Loading Bundle Name : {bundleInfo.BundleName}, loadURL {loadURL}, isLocalBundle : {islocalBundle}, isCached {isCached}");
                LoadedBundle previousBundle;

                if (s_AssetBundles.TryGetValue(bundleInfo.BundleName, out previousBundle) && previousBundle.Hash == bundleHash)
                {
                    if (LogMessages) Debug.Log($"Loading Bundle Name : {bundleInfo.BundleName} Complete - load skipped");
                }
                else
                {
                    var bundleReq = islocalBundle ? UnityWebRequestAssetBundle.GetAssetBundle(loadURL) : UnityWebRequestAssetBundle.GetAssetBundle(loadURL, bundleInfo.ToCachedBundle());
                    var operation = bundleReq.SendWebRequest();
                    while (!bundleReq.isDone)
                    {
                        result.SetProgress(operation.progress);
                        yield return null;
                    }

                    if (bundleReq.isNetworkError || bundleReq.isHttpError)
                    {
                        result.Done(BundleErrorCode.NetworkError);
                        yield break;
                    }

                    if (s_AssetBundles.TryGetValue(bundleInfo.BundleName, out previousBundle))
                    {
                        bundleReplaced = true;
                        previousBundle.Bundle.Unload(false);
                        if (previousBundle.RequestForReload != null)
                            previousBundle.RequestForReload.Dispose(); //dispose reload bundle
                        s_AssetBundles.Remove(bundleInfo.BundleName);
                    }

                    var loadedBundle = new LoadedBundle(bundleInfo, loadURL, DownloadHandlerAssetBundle.GetContent(bundleReq), islocalBundle);
                    s_AssetBundles.Add(bundleInfo.BundleName, loadedBundle);
                    CollectSceneNames(loadedBundle);
                    if (LogMessages) Debug.Log($"Loading Bundle Name : {bundleInfo.BundleName} Complete");
                    bundleReq.Dispose();
                }
            }

            //let's drop unknown bundles loaded
            foreach (var name in bundlesToUnload)
            {
                var bundleInfo = s_AssetBundles[name];
                bundleInfo.Bundle.Unload(false);
                if (bundleInfo.RequestForReload != null)
                    bundleInfo.RequestForReload.Dispose(); //dispose reload bundle
                s_AssetBundles.Remove(bundleInfo.Name);
            }

            //bump entire bundles' usage timestamp
            //we use manifest directly to find out entire list
            for (int i = 0; i < manifest.BundleInfos.Count; i++)
            {
                var cachedInfo = manifest.BundleInfos[i].ToCachedBundle();
                if (Caching.IsVersionCached(cachedInfo)) Caching.MarkAsUsed(cachedInfo);
            }

            var prevSpace = Caching.defaultCache.spaceOccupied;
            Caching.ClearCache(600); //as we bumped entire list right before clear, let it be just 600
            if (LogMessages) Debug.Log($"Cache CleanUp : {prevSpace} -> {Caching.defaultCache.spaceOccupied} bytes");

            PlayerPrefs.SetString("CachedManifest", JsonUtility.ToJson(manifest));
            GlobalBundleHash = manifest.GlobalHashString;
            result.Result = bundleReplaced;
            result.Done(BundleErrorCode.Success);
        }

        /// <summary>
        /// representation of loaded bundle
        /// </summary>
        private class LoadedBundle
        {
            public string Name;
            public AssetBundle Bundle;
            public Hash128 Hash;
            public List<string> Dependencies; //including self
            public bool IsLocalBundle;
            public string LoadPath;
            public UnityWebRequest RequestForReload;
            public bool IsReloading = false;
            public bool IsDisposed { get; private set; } = false;

            //constructor for editor
            public LoadedBundle(string name) => Name = name;

            public LoadedBundle(AssetBundleBuildManifest.BundleInfo info, string loadPath, AssetBundle bundle, bool isLocal)
            {
                Name = info.BundleName;
                IsLocalBundle = isLocal;
                LoadPath = loadPath;
                Bundle = bundle;
                Hash = Hash128.Parse(info.HashString);
                Dependencies = info.Dependencies;
                Dependencies.Add(Name);
            }

            public void Dispose()
            {
                if (!IsDisposed)
                {
                    Bundle?.Unload(false);
                    RequestForReload?.Dispose();
                    IsDisposed = true;
                }
            }
        }

        private struct SceneInfo {
            public string Name;
            public string Path;
            public LoadedBundle LoadedBundle;
        }

        //helper class for coroutine and callbacks
        private class BundleManagerHelper : MonoBehaviour
        {
            private void LateUpdate()
            {
                BundleManager.Update(false);
            }

            private void OnDestroy()
            {
                BundleManager.OnDestroy();
            }
        }
    }
}
