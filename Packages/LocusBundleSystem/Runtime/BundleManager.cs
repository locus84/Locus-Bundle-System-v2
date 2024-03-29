using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using System.Linq;

namespace BundleSystem
{
    /// <summary>
    /// representation of loaded bundle
    /// </summary>
    internal class LoadedBundle
    {
        public string Name;
        public AssetBundle Bundle;
        public Hash128 Hash;
        public List<string> Dependencies; //including self
        public bool IsLocalBundle;
        public string LoadPath;
        public UnityWebRequest CachedRequest;
        public int ReferenceCount;
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

        public void Dispose(bool unloadAllLoadedAssets = false)
        {
            if (!IsDisposed)
            {
                if(Bundle != null)
                {
                    Bundle.Unload(unloadAllLoadedAssets);
                    Bundle = null;
                }
                if(CachedRequest != null)
                {
                    CachedRequest.Dispose();
                    CachedRequest = null;
                }
                IsDisposed = true;
            }
        }
    }

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
        private static int s_InGameIncrementalVersion = 0;

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
        public static AssetBundleBuildManifest Manifest { get; private set; }


        private static string s_UserRemoteURL;
        private static string s_BundleBuildTarget;
        private static string s_DefaultRemoteURL;

        /// <summary>
        /// Set base remote url, it should be form of https://www.example.com/mypatch.
        /// build target string will be automatically appended.
        /// Full remote url will be https://www.example.com/mypatch/[BuildTarget]
        /// </summary>
        /// <param name="url">base url</param>
        public static void SetBaseRemoteURL(string url) => s_UserRemoteURL = url;

        /// <summary>
        /// Get current full remote url. ex) https://www.example.com/mypatch/[BuildTarget]
        /// </summary>
        /// <returns></returns>
        public static string GetFullRemoteURL()
        {
            //it has to be initialized
            if (!Initialized)
            {
                Debug.LogError("Do Initialize first");
                return string.Empty;
            }
            
#if UNITY_EDITOR
            //editor functionality
            if (s_EditorDatabaseMap.UseOuputAsRemote)
            {
                return "file://" + s_EditorDatabaseMap.OutputPath;
            }
#endif

            //if user custom remote url is empty
            if(string.IsNullOrWhiteSpace(s_UserRemoteURL)) 
            {
                return Utility.CombinePath(s_DefaultRemoteURL, s_BundleBuildTarget);
            }

            //build actual url
            return Utility.CombinePath(s_UserRemoteURL, s_BundleBuildTarget);
        }
        
        public static string GlobalBundleHash { get; private set; }
        public static bool LogMessages { get; set; }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void DomainReloaded()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;

            UnityMainThreadId = default;
            s_Helper = default;

            s_AssetBundles.Clear();
            s_LocalBundles.Clear();
            s_SceneInfos.Clear();
            s_ReleaseableBundles.Clear();
            s_InGameIncrementalVersion = 0;

            UseAssetDatabaseMap = true;
            s_EditorDatabaseMap = default;
            
            Initialized = false;
            LocalURL = default;
            Manifest = default;

            s_UserRemoteURL = default;
            s_BundleBuildTarget = default;
            s_DefaultRemoteURL = default;

            GlobalBundleHash = default;
            LogMessages = default;

            s_LastTrackId = default;
            s_SceneObjectDummy = new Texture2D(0,0) { name = "SceneDummy" };
            s_LoadingObjectDummy = new Texture2D(0,0) { name = "LoadingDummy" };
            s_TrackInfoDict.Clear();
            s_TrackInstanceTransformDict.Clear();
            s_SceneHandles.Clear();
            s_SceneRootObjectCache.Clear();
            s_LastLoadedScene = default;

            s_CurrentReloadingCount = default;
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Setup()
        {
            UnityMainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
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
        /// <returns>async operation that can be yield return</returns>
        public static BundleAsyncOperation Initialize()
        {
            var result = new BundleAsyncOperation();
            s_Helper.StartCoroutine(CoInitalizeLocalBundles(result));
            return result;
        }

        static IEnumerator CoInitalizeLocalBundles(BundleAsyncOperation result)
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
                Manifest = GetEditorDefaultManifest();
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
            if (!Utility.CheckRequestSuccess(manifestReq))
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

                if (Utility.CheckRequestSuccess(bundleReq))
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

            s_BundleBuildTarget = localManifest.BuildTarget;
            s_DefaultRemoteURL = localManifest.DefaultRemoteURL;

            Initialized = true;
            Manifest = localManifest;

            if (LogMessages) Debug.Log($"Initialize Success \nLocal URL : {LocalURL}");
            
            GlobalBundleHash = localManifest.GlobalHashString;

            //increase version
            s_InGameIncrementalVersion++;
            result.Done(BundleErrorCode.Success);
        }

#if UNITY_EDITOR
        static AssetBundleBuildManifest GetEditorDefaultManifest()
        {
            return new AssetBundleBuildManifest() { UserVersionString = s_EditorDatabaseMap.UserVersionString };
        }
#endif

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
                result.Result = GetEditorDefaultManifest();
                result.Done(BundleErrorCode.Success);
                yield break;
            }
#endif

            var remoteURL = GetFullRemoteURL();
            var manifestReq = UnityWebRequest.Get(Utility.CombinePath(remoteURL, ManifestFileName));
            yield return manifestReq.SendWebRequest();

            if (!Utility.CheckRequestSuccess(manifestReq))
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
        public static BundleDonwloadAsyncOperation DownloadAssetBundles(AssetBundleBuildManifest manifest, IEnumerable<string> subsetNames = null)
        {
            var result = new BundleDonwloadAsyncOperation();
            s_Helper.StartCoroutine(CoDownloadAssetBundles(manifest, subsetNames, result));
            return result;
        }

        static IEnumerator CoDownloadAssetBundles(AssetBundleBuildManifest manifest, IEnumerable<string> subsetNames, BundleDonwloadAsyncOperation result)
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

            result.BundlesToUnload = new HashSet<string>(s_AssetBundles.Keys);
            result.BundlesToAddOrReplace = new List<LoadedBundle>();
            result.BundlesWillReplaced = new List<string>();

            var remoteURL = GetFullRemoteURL();
            var downloadBundleList = subsetNames == null ? manifest.BundleInfos : manifest.CollectSubsetBundleInfoes(subsetNames);

            result.SetIndexLength(downloadBundleList.Count);

            for (int i = 0; i < downloadBundleList.Count; i++)
            {
                result.SetCurrentIndex(i);
                var bundleInfo = downloadBundleList[i];
                var bundleHash = Hash128.Parse(bundleInfo.HashString);

                //remove from the set so we can track bundles that should be cleared
                result.BundlesToUnload.Remove(bundleInfo.BundleName);

                var islocalBundle = s_LocalBundles.TryGetValue(bundleInfo.BundleName, out var localHash) && localHash == bundleHash;
                var isCached = Caching.IsVersionCached(bundleInfo.ToCachedBundle());
                result.SetCachedBundle(isCached);

                var loadURL = islocalBundle ? Utility.CombinePath(LocalURL, bundleInfo.BundleName) : Utility.CombinePath(remoteURL, bundleInfo.BundleName);
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
                    while (!bundleReq.isDone && !result.IsCancelled)
                    {
                        result.SetProgress(operation.progress);
                        yield return null;
                    }

                    if(result.IsCancelled)
                    {
                        //dispose currentRequest
                        bundleReq.Dispose();
                        yield break;
                    }

                    if (!Utility.CheckRequestSuccess(bundleReq))
                    {
                        //dispose currentRequest
                        bundleReq.Dispose();
                        result.Done(BundleErrorCode.NetworkError);
                        yield break;
                    }

                    //examin current bundle should be replaced or not
                    if(s_AssetBundles.ContainsKey(bundleInfo.BundleName))
                    {
                        result.BundlesWillReplaced.Add(bundleInfo.BundleName);
                    }

                    //pass req as cachedRequest
                    var loadedBundle = new LoadedBundle(bundleInfo, loadURL, null, islocalBundle) { CachedRequest = bundleReq };

                    result.BundlesToAddOrReplace.Add(loadedBundle);
                    if (LogMessages) Debug.Log($"Loading Bundle Name : {bundleInfo.BundleName} Complete");
                }
            }

            result.Manifest = manifest;
            result.Version = s_InGameIncrementalVersion + 1;
            result.Done(BundleErrorCode.Success);
        }

        internal static bool ApplyDownloadOperationInternal(BundleDonwloadAsyncOperation operation, bool additive, bool unloadAll, bool cleanUpCache)
        {
#if UNITY_EDITOR
            if (UseAssetDatabaseMap) return true;
#endif

            //download operation always should be sequential, as it's based on previous state,
            //keep track of previous version and see if it's valid operation.
            if(operation.Version != s_InGameIncrementalVersion + 1)
            {
                if(LogMessages) Debug.LogError("The operation version is invalid, please dispose and try again");
                return false;
            }

            //update bundles
            foreach(var bundle in operation.BundlesToAddOrReplace)
            {
                //remove previous bundles
                if (s_AssetBundles.TryGetValue(bundle.Name, out var previousBundle)) previousBundle.Dispose(unloadAll);

                //cached request to bundle 
                bundle.Bundle = DownloadHandlerAssetBundle.GetContent(bundle.CachedRequest);
                bundle.CachedRequest.Dispose();
                bundle.CachedRequest = null;

                //add and collect scene names
                s_AssetBundles[bundle.Name] = bundle;
                CollectSceneNames(bundle);
            }
            
            //if additive, keep bundles in the full bundleinfo list
            if(additive)
            {
                foreach(var info in operation.Manifest.BundleInfos)
                {
                    operation.BundlesToUnload.Remove(info.BundleName);
                }
            }
            
            //let's drop unknown bundles loaded
            foreach (var name in operation.BundlesToUnload)
            {
                var bundleInfo = s_AssetBundles[name];
                bundleInfo.Dispose();
                s_AssetBundles.Remove(bundleInfo.Name);
            }
            
            //bump entire bundles' usage timestamp
            //we use manifest directly to find out entire list
            foreach(var info in operation.Manifest.BundleInfos)
            {
                var cachedInfo = info.ToCachedBundle();
                if (Caching.IsVersionCached(cachedInfo)) Caching.MarkAsUsed(cachedInfo);
            }

            //if user wants cleanup
            if(cleanUpCache)
            {
                var prevSpace = Caching.defaultCache.spaceOccupied;
                Caching.ClearCache(600); //as we bumped entire list right before clear, let it be just 600
                if (LogMessages) Debug.Log($"Cache CleanUp : {prevSpace} -> {Caching.defaultCache.spaceOccupied} bytes");
            }

            PlayerPrefs.SetString("CachedManifest", JsonUtility.ToJson(operation.Manifest));
            s_InGameIncrementalVersion = operation.Version;
            Manifest = operation.Manifest;

            GlobalBundleHash = operation.Manifest.GlobalHashString;
            
            return true;
        }

        internal static void CancelDownloadOperationInternal(BundleDonwloadAsyncOperation operation)
        {
#if UNITY_EDITOR
            if (UseAssetDatabaseMap) return;
#endif
            //nothing has done while downloading
            if(operation.BundlesToAddOrReplace == null) return;

            //dispose already loaded requests
            foreach(var bundle in operation.BundlesToAddOrReplace) 
            {
                //it can be null if already applied
                bundle.CachedRequest?.Dispose();
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
