using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BundleSystem
{
    public static partial class BundleManager
    {
        public static BundleSyncRequests<T> LoadAll<T>(this Component owner, string bundleName) where T : Object
        {
#if UNITY_EDITOR
            if (UseAssetDatabaseMap)
            {
                EnsureAssetDatabase();
                if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return BundleSyncRequests<T>.Empty;
                var assets = s_EditorDatabaseMap.GetAssetPaths(bundleName);
                if (assets.Count == 0) return BundleSyncRequests<T>.Empty;

                var typeExpected = typeof(T);
                var foundList = new List<T>(assets.Count);

                for (int i = 0; i < assets.Count; i++)
                {
                    var loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assets[i]);
                    if (loaded == null) continue;
                    foundList.Add(loaded);
                }

                var loadedAssets = foundList.ToArray();
                var handles = TrackObjects<T>(owner, loadedAssets, foundBundle);
                return new BundleSyncRequests<T>(loadedAssets, handles);
            }
            else
#endif
            {
                if (!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
                if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return BundleSyncRequests<T>.Empty;
                var loadedAssets = foundBundle.Bundle.LoadAllAssets<T>();
                var handles = TrackObjects<T>(owner, loadedAssets, foundBundle);
                return new BundleSyncRequests<T>(loadedAssets, handles);
            }
        }


        public static BundleSyncRequest<T> Load<T>(this Component owner, string bundleName, string assetName) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (UseAssetDatabaseMap)
            {
                EnsureAssetDatabase();
                if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return BundleSyncRequest<T>.Empty;
                var assetPath = s_EditorDatabaseMap.GetAssetPath<T>(bundleName, assetName);
                if (string.IsNullOrEmpty(assetPath)) return BundleSyncRequest<T>.Empty; //asset not exist
                var loadedAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath);
                if (loadedAsset == null) return BundleSyncRequest<T>.Empty;
                return new BundleSyncRequest<T>(loadedAsset, TrackObject<T>(loadedAsset, foundBundle));
            }
            else
#endif
            {
                if (!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
                if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return BundleSyncRequest<T>.Empty; ;
                var loadedAsset = foundBundle.Bundle.LoadAsset<T>(assetName);
                if (loadedAsset == null) return BundleSyncRequest<T>.Empty;
                return new BundleSyncRequest<T>(loadedAsset, TrackObject<T>(loadedAsset, foundBundle));
            }
        }


        public static BundleSyncRequests<T> LoadWithSubAssets<T>(this Component owner, string bundleName, string assetName) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (UseAssetDatabaseMap)
            {
                EnsureAssetDatabase();
                if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return BundleSyncRequests<T>.Empty;
                var assetPath = s_EditorDatabaseMap.GetAssetPath<T>(bundleName, assetName);
                if (string.IsNullOrEmpty(assetPath)) return BundleSyncRequests<T>.Empty;
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath);
                var loadedAssets = assets.Select(a => a as T).Where(a => a != null).ToArray();
                var handles = TrackObjects<T>(owner, loadedAssets, foundBundle);
                return new BundleSyncRequests<T>(loadedAssets, handles);
            }
            else
#endif
            {
                if (!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
                if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return BundleSyncRequests<T>.Empty;
                var loadedAssets = foundBundle.Bundle.LoadAssetWithSubAssets<T>(assetName);
                var handles = TrackObjects<T>(owner, loadedAssets, foundBundle);
                return new BundleSyncRequests<T>(loadedAssets, handles);
            }
        }


        public static BundleAsyncRequest<T> LoadAsync<T>(string bundleName, string assetName) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (UseAssetDatabaseMap)
            {
                EnsureAssetDatabase();
                if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return BundleAsyncRequest<T>.Empty; //asset not exist
                var assetPath = s_EditorDatabaseMap.GetAssetPath<T>(bundleName, assetName);
                if (string.IsNullOrEmpty(assetPath)) return BundleAsyncRequest<T>.Empty; //asset not exist
                var loadedAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath);
                if (loadedAsset == null) return BundleAsyncRequest<T>.Empty; //asset not exist
                var handle = TrackObject<T>(loadedAsset, foundBundle);
                return new BundleAsyncRequest<T>(loadedAsset, handle);
            }
            else
#endif
            {
                if (!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
                if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return BundleAsyncRequest<T>.Empty; //asset not exist
                var request = foundBundle.Bundle.LoadAssetAsync<T>(assetName);
                //need to keep bundle while loading, so we retain before load, release after load
                var handle = TrackObject<T>(s_LoadingObjectDummy, foundBundle);
                var bundleRequest = new BundleAsyncRequest<T>(request, handle);
                request.completed += op => AsyncAssetLoaded(handle, request, bundleRequest);
                return new BundleAsyncRequest<T>(request, handle);
            }
        }

        private static void AsyncAssetLoaded<T>(TrackHandle<T> handle, AssetBundleRequest request, BundleAsyncRequest<T> bundleRequest) where T : Object
        {
            //loading tracks are not being released, so there must be.
            var info = s_TrackInfoDict[handle.Id];
            info.Asset = request.asset;
            info.LoadTime = Time.realtimeSinceStartup;
            
            //force release null request
            if(info.Asset == null) info.Status = TrackStatus.ReleaseRequested;
            s_TrackInfoDict[handle.Id] = info;
        }

        public static Scene LoadScene(string sceneNameOrPath, LoadSceneMode mode)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) throw new System.Exception("This function does not support non-playing mode!");
            if (UseAssetDatabaseMap)
            {
                EnsureAssetDatabase();
                //like default scene load functionality, we return null if something went wrong
                if (!TryGetSceneInfo(sceneNameOrPath, out var info))
                {
                    Debug.LogError("Bundle you requested could not be found");
                    return default;
                }
                var scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(info.Path, new LoadSceneParameters(mode));
                if(scene.IsValid()) 
                {
                    RetainBundle(info.LoadedBundle);
                    s_SceneHandles.Add(scene.handle, info.LoadedBundle);
                }
                return scene;
            }
            else
#endif
            {
                if (!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
                //like default scene load functionality, we return null if something went wrong
                if (!TryGetSceneInfo(sceneNameOrPath, out var info))
                {
                    Debug.LogError("Bundle you requested could not be found");
                    return default;
                }
                var scene = SceneManager.LoadScene(sceneNameOrPath, new LoadSceneParameters(mode));
                if(scene.IsValid()) 
                {
                    RetainBundle(info.LoadedBundle);
                    s_SceneHandles.Add(scene.handle, info.LoadedBundle);
                }
                return scene;
            }
        }

        public static BundleAsyncSceneRequest LoadSceneAsync(string sceneNameOrPath, LoadSceneMode mode)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) throw new System.Exception("This function does not support non-playing mode!");
            if (UseAssetDatabaseMap)
            {
                EnsureAssetDatabase();
                //like default scene load functionality, we return null if something went wrong
                if (!TryGetSceneInfo(sceneNameOrPath, out var info))
                {
                    Debug.LogError("Bundle you requested could not be found");
                    return BundleAsyncSceneRequest.Failed;
                }
                var aop = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(info.Path, new LoadSceneParameters(mode));
                if (aop == null) return BundleAsyncSceneRequest.Failed; // scene cannot be loaded
                
                var result = new BundleAsyncSceneRequest(aop);

                //this retain released at scene unload
                RetainBundle(info.LoadedBundle);
                aop.completed += op => 
                {
                    s_SceneHandles.Add(s_LastLoadedScene.handle, info.LoadedBundle);
                    OnSceneLoaded(s_LastLoadedScene, mode);
                    result.Scene = s_LastLoadedScene;
                };

                return result;
            }
            else
#endif
            {
                if (!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");

                //like default scene load functionality, we return null if something went wrong
                if (!TryGetSceneInfo(sceneNameOrPath, out var info))
                {
                    Debug.LogError("Bundle you requested could not be found");
                    return BundleAsyncSceneRequest.Failed;
                }
                
                var aop = SceneManager.LoadSceneAsync(sceneNameOrPath, mode);
                if (aop == null) return BundleAsyncSceneRequest.Failed;

                var result = new BundleAsyncSceneRequest(aop);

                //this retain released at scene unload
                RetainBundle(info.LoadedBundle);
                aop.completed += op => 
                {
                    s_SceneHandles.Add(s_LastLoadedScene.handle, info.LoadedBundle);
                    OnSceneLoaded(s_LastLoadedScene, mode);
                    result.Scene = s_LastLoadedScene;
                };


                return result;
            }
        }

        private static bool TryGetSceneInfo(string sceneNameOrPath, out SceneInfo info)
        {
            //check nameOrPathExist
            if (!s_SceneInfos.TryGetValue(sceneNameOrPath, out info)) return false;

            //check if disposed
            if(info.LoadedBundle.IsDisposed)
            {
                s_SceneInfos.Remove(sceneNameOrPath);
                info = default;
                return false;
            }

            return true;
        }

        public static bool IsAssetExist(string bundleName, string assetName)
        {
#if UNITY_EDITOR
            if (UseAssetDatabaseMap)
            {
                EnsureAssetDatabase();
                return s_EditorDatabaseMap.IsAssetExist(bundleName, assetName);
            }
            else
#endif
            {
                if (!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
                if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return false;
                return foundBundle.Bundle.Contains(assetName);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CheckInstantiableHandle(int trackId, out LoadedBundle bundle, out TrackInfo info)
        {
            if (!s_TrackInfoDict.TryGetValue(trackId, out info)) throw new System.Exception("Handle is not valid");
            //it's needed as the bundle might be dispoedand we need new one
            if (info.LoadedBundle.IsDisposed)
            {
                if (!s_AssetBundles.TryGetValue(info.LoadedBundle.Name, out bundle)) throw new System.Exception("Bundle is not found");
            }
            else
            {
                bundle = info.LoadedBundle;
            }
            if (info.Asset == s_LoadingObjectDummy) throw new System.Exception("Asset is currently loading");
        }

        public static GameObject Instantiate(this TrackHandle<GameObject> handle)
        {
            CheckInstantiableHandle(handle.Id, out var bundle, out var info);
            var instance = GameObject.Instantiate(info.Asset as GameObject);
            TrackInstanceObject<GameObject>(instance.transform, info.Asset, bundle);
            return instance;
        }

        public static GameObject Instantiate(this TrackHandle<GameObject> handle, Transform parent)
        {
            CheckInstantiableHandle(handle.Id, out var bundle, out var info);
            var instance = GameObject.Instantiate(info.Asset as GameObject, parent);
            TrackInstanceObject<GameObject>(instance.transform, info.Asset, bundle);
            return instance;
        }

        public static GameObject Instantiate(this TrackHandle<GameObject> handle, Transform parent, bool instantiateInWorldSpace)
        {
            CheckInstantiableHandle(handle.Id, out var bundle, out var info);
            var instance = GameObject.Instantiate(info.Asset as GameObject, parent, instantiateInWorldSpace);
            TrackInstanceObject<GameObject>(instance.transform, info.Asset, bundle);
            return instance;
        }

        public static GameObject Instantiate(this TrackHandle<GameObject> handle, Vector3 position, Quaternion rotation)
        {
            CheckInstantiableHandle(handle.Id, out var bundle, out var info);
            var instance = GameObject.Instantiate(info.Asset as GameObject, position, rotation);
            TrackInstanceObject<GameObject>(instance.transform, info.Asset, bundle);
            return instance;
        }

        public static GameObject Instantiate(this TrackHandle<GameObject> handle, Vector3 position, Quaternion rotation, Transform parent)
        {
            CheckInstantiableHandle(handle.Id, out var bundle, out var info);
            var instance = GameObject.Instantiate(info.Asset as GameObject, position, rotation, parent);
            TrackInstanceObject<GameObject>(instance.transform, info.Asset, bundle);
            return instance;
        }
    }
}