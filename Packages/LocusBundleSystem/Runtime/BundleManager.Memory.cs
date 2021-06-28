﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace BundleSystem
{
    public struct TrackHandle<T> where T : Object
    {
        /// <summary>
        /// Track handle id for internal usage
        /// </summary>
        public int Id { get; private set; }
        internal TrackHandle(int id) => Id = id;

        /// <summary>
        /// Is this handle valid and tracked by bundle manager?
        /// </summary>
        /// <returns></returns>
        public bool IsValid() => Id != 0 && BundleManager.IsTrackHandleValidInternal(Id);

        /// <summary>
        /// Invalid track handle
        /// </summary>
        public static TrackHandle<T> Invalid => new TrackHandle<T>(0);

        /// <summary>
        /// Release this handle. Does not immediately release related bundles.
        /// Use BundleManager.UpdateImmediate() for immediate bundle reloading.
        /// </summary>
        public void Release()
        {
            BundleManager.RequestReleaseHandleInternal(Id);
            Id = 0;
        }
    }

    public static partial class BundleManager
    {
        static int s_LastTrackId = 0;
        static Object s_SceneObjectDummy = new Texture2D(0,0) { name = "SceneDummy" };
        static Object s_LoadingObjectDummy = new Texture2D(0,0) { name = "LoadingDummy" };
        static IndexedDictionary<int, TrackInfo> s_TrackInfoDict = new IndexedDictionary<int, TrackInfo>(10);
        static Dictionary<int, int> s_TrackInstanceTransformDict = new Dictionary<int, int>(10);

        //bundle ref count
        private static Dictionary<string, int> s_BundleRefCounts = new Dictionary<string, int>(10);
        
        /// <summary>
        /// Get current tracking information dictionary
        /// </summary>
        /// <returns>Tracking information dictionary</returns>
        public static Dictionary<int, TrackInfo> GetTrackingSnapshot()
        {
            var targetDict = new Dictionary<int, TrackInfo>();
            if(Application.isPlaying) s_TrackInfoDict.FillNormalDictionary(targetDict);
            return targetDict;
        } 

        /// <summary>
        /// Get current tracking information dictionary, without allocation
        /// </summary>
        /// <param name="targetDict">Dictionary to fill</param>
        public static void GetTrackingSnapshotNonAlloc(Dictionary<int, TrackInfo> targetDict)
        {
            targetDict.Clear();
            if(Application.isPlaying) s_TrackInfoDict.FillNormalDictionary(targetDict);
        } 

        /// <summary>
        /// Assign new owner of track handle
        /// </summary>
        /// <param name="handle">Target handle</param>
        /// <param name="newOwner">New owner</param>
        public static void ChangeOwner<T>(this TrackHandle<T> handle, Component newOwner) where T : Object
        {
            if(!newOwner.gameObject.scene.IsValid()) throw new System.Exception("Owner must be scene object");
            if(!handle.IsValid()) throw new System.Exception("Handle is not valid or already not tracked");
            var exitingTrackInfo = s_TrackInfoDict[handle.Id];
            exitingTrackInfo.Owner = newOwner;
            s_TrackInfoDict[handle.Id] = exitingTrackInfo;
        }

        internal static bool IsTrackHandleValidInternal(int id) => id != 0 && s_TrackInfoDict.ContainsKey(id);
        internal static void SupressAutoReleaseInternal(int id)
        {
            if(id != 0 && s_TrackInfoDict.TryGetValue(id, out var info) && !info.IsPinned)
            {
                info.Pin();
                s_TrackInfoDict[id] = info;
            }
        } 

        /// <summary>
        /// Track part of loaded asset.
        /// Used when you explicitely track an asset which does not directly loaded from bundle system.
        /// </summary>
        /// <param name="referenceHandle">Reference handle that loaded from same bundle</param>
        /// <param name="asset">Asset to track</param>
        /// <param name="newOwner">New owner if specified, shares same owner if not specified</param>
        /// <returns>Returns newly created track handle</returns>
        public static TrackHandle<T> TrackExplicit<TRef, T>(this TrackHandle<TRef> referenceHandle, T asset,  Component newOwner = null)
        where TRef : Object where T : Object
        {
            if(newOwner != null && !newOwner.gameObject.scene.IsValid()) throw new System.Exception("Owner must be scene object");
            if(!referenceHandle.IsValid()) throw new System.Exception("Handle is not valid or already not tracked");

            var exitingTrackInfo = s_TrackInfoDict[referenceHandle.Id];
            var newTrackId = ++s_LastTrackId;
            if(newOwner == null) newOwner = exitingTrackInfo.Owner;

            s_TrackInfoDict.Add(newTrackId, new TrackInfo()
            {
                BundleName = exitingTrackInfo.BundleName,
                Asset = asset,
                Owner = newOwner,
                LoadTime = float.MaxValue
            });

            return new TrackHandle<T>(newTrackId);
        }

        /// <summary>
        /// Release old track handle and assign new handle.
        /// </summary>
        /// <param name="newHandle">New handle</param>
        /// <param name="oldHandle">Old handle to release</param>
        public static void Override<T>(this TrackHandle<T> newHandle, ref TrackHandle<T> oldHandle) where T : Object
        {
            oldHandle.Release();
            oldHandle = newHandle;
        }

        /// <summary>
        /// Get instance track handle associated privided gameobject and it's parents
        /// </summary>
        /// <param name="gameObject">input game object</param>
        /// <returns>returns found track handle, invalid handle if not found</returns>
        public static TrackHandle<GameObject> GetInstanceTrackHandle(this GameObject gameObject) 
        {
            if(!gameObject.scene.IsValid()) throw new System.Exception("Input gameobject must be scene object");
            if(!TryGetTrackId(gameObject.transform, out var trackId))
            {
                return TrackHandle<GameObject>.Invalid;
            }
            return new TrackHandle<GameObject>(trackId);
        }

        static bool TryGetTrackId(Transform trans, out int trackId)
        {
            do
            {
                if(s_TrackInstanceTransformDict.TryGetValue(trans.GetInstanceID(), out trackId)) return true;
                trans = trans.parent;
            }
            while(trans != null);

            trackId = default;
            return false;
        }

        public struct TrackInfo
        {
            public Component Owner;
            public Object Asset;
            public string BundleName;
            public float LoadTime;
            public bool ReleaseRequested;
            public bool IsPinned => LoadTime >= float.MaxValue;
            public void Pin() => LoadTime = float.MaxValue;
        }

        private static TrackHandle<T> TrackObject<T>(Component owner, Object asset, LoadedBundle loadedBundle, bool pinInitially = false) where T : Object
        {
            if(!owner.gameObject.scene.IsValid()) throw new System.Exception("Owner must be scene object");
            var trackId = ++s_LastTrackId;
            s_TrackInfoDict.Add(trackId, new TrackInfo(){
                BundleName = loadedBundle.Name,
                Owner = owner,
                Asset = asset,
                LoadTime = pinInitially? float.MaxValue : Time.realtimeSinceStartup
            });

            RetainBundle(loadedBundle);
            return new TrackHandle<T>(trackId);
        }

        private static TrackHandle<T>[] TrackObjects<T>(Component owner, Object[] assets, LoadedBundle loadedBundle) where T : Object
        {
            if(!owner.gameObject.scene.IsValid()) throw new System.Exception("Owner must be scene object");
            var result = new TrackHandle<T>[assets.Length];
            for(int i = 0; i < assets.Length; i++)
            {
                var obj = assets[i];
                var trackId = ++s_LastTrackId;
                s_TrackInfoDict.Add(trackId, new TrackInfo()
                {
                    BundleName = loadedBundle.Name,
                    Owner = owner,
                    Asset = assets[i],
                    LoadTime = Time.realtimeSinceStartup
                });
            }

            if(assets.Length > 0) RetainBundle(loadedBundle, assets.Length); //do once

            return result;
        }

        internal static void RequestReleaseHandleInternal(int trackId)
        {
            if(trackId == 0) return;
            if(!s_TrackInfoDict.TryGetValue(trackId, out var info)) return;
            info.ReleaseRequested = true;
            s_TrackInfoDict[trackId] = info;
        }

        private static void RetainBundle(LoadedBundle bundle, int count = 1)
        {
#if UNITY_EDITOR
            if(UseAssetDatabaseMap)
            {
                if (!s_BundleRefCounts.ContainsKey(bundle.Name)) s_BundleRefCounts[bundle.Name] = count;
                else s_BundleRefCounts[bundle.Name] += count;
            }
            else
#endif
            {
                for (int i = 0; i < bundle.Dependencies.Count; i++)
                {
                    var refBundleName = bundle.Dependencies[i];
                    if (!s_BundleRefCounts.ContainsKey(refBundleName)) s_BundleRefCounts[refBundleName] = count;
                    else s_BundleRefCounts[refBundleName] += count;
                }
            }
        }

        private static void ReleaseBundle(LoadedBundle bundle, int count = 1)
        {
#if UNITY_EDITOR
            if(UseAssetDatabaseMap)
            {
                var nextCount = s_BundleRefCounts[bundle.Name] - count;
                if(nextCount == 0) s_BundleRefCounts.Remove(bundle.Name);
                else s_BundleRefCounts[bundle.Name] = nextCount;
            }
            else
#endif
            {
                for (int i = 0; i < bundle.Dependencies.Count; i++)
                {
                    var refBundleName = bundle.Dependencies[i];
                    if (s_BundleRefCounts.ContainsKey(refBundleName))
                    {
                        s_BundleRefCounts[refBundleName] -= count;
                        if (s_BundleRefCounts[refBundleName] <= 0) 
                        {
                            s_BundleRefCounts.Remove(refBundleName);
                            ReloadBundle(refBundleName);
                        }
                    }
                }
            }
        }

        static List<GameObject> s_SceneRootObjectCache = new List<GameObject>();
        private static void TrackOnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            //if scene is from assetbundle, path will be assetpath inside bundle
            if (s_SceneNames.TryGetValue(scene.path, out var loadedBundle))
            {
                RetainBundle(loadedBundle);
                scene.GetRootGameObjects(s_SceneRootObjectCache);
                for (int i = 0; i < s_SceneRootObjectCache.Count; i++)
                {
                    var owner = s_SceneRootObjectCache[i].transform;
                    var handle = TrackObject<Object>(owner, s_SceneObjectDummy, loadedBundle, true);
                    s_TrackInstanceTransformDict.Add(owner.GetInstanceID(), handle.Id);
                }
                s_SceneRootObjectCache.Clear();
            }
        }

        private static void TrackOnSceneUnLoaded(UnityEngine.SceneManagement.Scene scene)
        {
            //if scene is from assetbundle, path will be assetpath inside bundle
            if (s_SceneNames.TryGetValue(scene.path, out var loadedBundle))
            {
                ReleaseBundle(loadedBundle);
            }
        }

        public static void UpdateImmediate()
        {
            Update(true);
        }

        private static void Update(bool immediate)
        {
            //we should check entire collection at least in 5 seconds, calculate trackCount for that purpose
            int trackCount;
            if(immediate)
            {
                s_TrackInfoDict.ResetCurrentIndex();
                trackCount = s_TrackInfoDict.Count;
            }
            else
            {
                trackCount = Mathf.CeilToInt(Time.unscaledDeltaTime * 0.2f * s_TrackInfoDict.Count);
            }

            for(int i = 0; i < trackCount; i++)
            {
                //maybe empty
                if (!s_TrackInfoDict.TryGetNext(out var kv)) break;
                //we don't want to release bundle while we're loading from it
                if (kv.Value.Asset == s_LoadingObjectDummy) continue;

                //calculate time
                var timePassed = kv.Value.LoadTime <= Time.realtimeSinceStartup - (immediate? 0f: 1f);
                
                //not valid cases
                if (!kv.Value.ReleaseRequested && !timePassed && kv.Value.Owner != null) continue;
                
                s_TrackInfoDict.Remove(kv.Key);
                var instanceId = kv.Value.Owner.GetInstanceID();
                if(s_TrackInstanceTransformDict.TryGetValue(instanceId, out var foundId) && foundId == kv.Key) 
                {
                    s_TrackInstanceTransformDict.Remove(instanceId);
                }

                if (s_AssetBundles.TryGetValue(kv.Value.BundleName, out var loadedBundle)) 
                {
                    ReleaseBundle(loadedBundle);
                }
            }
        }

        private static void ReloadBundle(string bundleName)
        {
            if (!s_AssetBundles.TryGetValue(bundleName, out var loadedBundle))
            {
                if (LogMessages) Debug.Log("Bundle To Reload does not exist");
                return;
            }

            if(loadedBundle.IsReloading)
            {
                if (LogMessages) Debug.Log("Bundle is already reloading");
                return;
            }

            if(loadedBundle.RequestForReload != null)
            {
                loadedBundle.Bundle.Unload(true);
                loadedBundle.Bundle = DownloadHandlerAssetBundle.GetContent(loadedBundle.RequestForReload);
                //stored request needs to be disposed
                loadedBundle.RequestForReload.Dispose();
                loadedBundle.RequestForReload = null;
            }
            else
            {
                s_Helper.StartCoroutine(CoReloadBundle(bundleName, loadedBundle));
            }
        }

        static IEnumerator CoReloadBundle(string bundleName, LoadedBundle loadedBundle)
        {
            if (LogMessages) Debug.Log($"Start Reloading Bundle {bundleName}");
            var bundleReq = loadedBundle.IsLocalBundle? UnityWebRequestAssetBundle.GetAssetBundle(loadedBundle.LoadPath) : 
                UnityWebRequestAssetBundle.GetAssetBundle(loadedBundle.LoadPath, new CachedAssetBundle(bundleName, loadedBundle.Hash));

            loadedBundle.IsReloading = true;
            yield return bundleReq.SendWebRequest();
            loadedBundle.IsReloading = false;

            if (bundleReq.isNetworkError || bundleReq.isHttpError)
            {
                Debug.LogError($"Bundle reload error { bundleReq.error }");
                yield break;
            }

            if(loadedBundle.IsDisposed)
            {
                if (LogMessages) Debug.Log("Bundle To Reload does not exist(dispoed during reloaing)");
                bundleReq.Dispose();
                yield break;
            }

            //if we can swap now
            if(!s_BundleRefCounts.TryGetValue(bundleName, out var refCount) || refCount == 0)
            {
                if (LogMessages) Debug.Log($"Reloaded Bundle {bundleName}");
                loadedBundle.Bundle.Unload(true);
                loadedBundle.Bundle = DownloadHandlerAssetBundle.GetContent(bundleReq);
                bundleReq.Dispose();
            }
            else
            {
                if (LogMessages) Debug.Log($"Reloaded Bundle Cached for later use {bundleName}");
                //store request for laster use
                loadedBundle.RequestForReload = bundleReq;
            }
        }
    }
}

