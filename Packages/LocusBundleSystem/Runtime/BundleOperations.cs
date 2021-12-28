
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BundleSystem
{
    /// <summary>
    /// Synchronized Asset Load Request with Type T
    /// </summary>
    public struct BundleSyncRequest<T> : System.IDisposable where T : Object
    {
        internal readonly static BundleSyncRequest<T> Empty = new BundleSyncRequest<T>(null, TrackHandle<T>.Invalid);

        /// <summary>
        /// Loaded Asset's TrackHandle.
        /// </summary>
        public readonly TrackHandle<T> Handle;

        /// <summary>
        /// Actual loaded Asset.
        /// </summary>
        public readonly T Asset;

        internal BundleSyncRequest(T asset, TrackHandle<T> handle)
        {
            Asset = asset;
            Handle = handle;
        }

        /// <summary>
        /// Supress asset's auto release.
        /// </summary>
        public BundleSyncRequest<T> Pin()
        {
            BundleManager.SupressAutoReleaseInternal(Handle.Id);
            return this;
        }

        /// <summary>
        /// Release loaded asset. This is same with calling Release() function of TrackHandle.
        /// </summary>
        public void Dispose()
        {
            Handle.Release();
        }
    }

    /// <summary>
    /// Synchronized multiple Asset Load Request with Type T
    /// </summary>
    public struct BundleSyncRequests<T> : System.IDisposable where T : Object
    {
        internal readonly static BundleSyncRequests<T> Empty = new BundleSyncRequests<T>(new T[0], new TrackHandle<T>[0]);
        /// <summary>
        /// The loaded Assets' TrackHandles.
        /// </summary>
        public readonly TrackHandle<T>[] Handles;
        /// <summary>
        /// Actual loaded Assets.
        /// </summary>
        public readonly T[] Assets;

        internal BundleSyncRequests(T[] assets, TrackHandle<T>[] handles)
        {
            Assets = assets;
            Handles = handles;
        }
        
        /// <summary>
        /// Supress all asset's auto release.
        /// </summary>
        public BundleSyncRequests<T> Pin()
        {
            for(int i = 0; i < Handles.Length; i++)
            {
                BundleManager.SupressAutoReleaseInternal(Handles[i].Id);
            }
            return this;
        }

        /// <summary>
        /// Release all assets loaded. This is same with calling Release() function of TrackHandle.
        /// </summary>
        public void Dispose()
        {
            for(int i = 0; i < Handles.Length; i++)
            {
                Handles[i].Release();
            }
        }
    }

    /// <summary>
    /// Async Asset load request.
    /// using this class we can provide unified structure.
    /// </summary>
    /// <typeparam name="T">Type of the asset the load</typeparam>
    public class BundleAsyncRequest<T> : CustomYieldInstruction, IAwaiter<BundleAsyncRequest<T>>, System.IDisposable where T : Object
    {
        internal readonly static BundleAsyncRequest<T> Empty = new BundleAsyncRequest<T>((T)null, TrackHandle<T>.Invalid);
        
        /// <summary>
        /// Loaded Asset's TrackHandle.
        /// </summary>
        public readonly TrackHandle<T> Handle;
        AssetBundleRequest m_Request;
        T m_LoadedAsset;

        /// <summary>
        /// actual assetbundle request warpper
        /// </summary>
        /// <param name="request"></param>
        internal BundleAsyncRequest(AssetBundleRequest request, TrackHandle<T> handle)
        {
            m_Request = request;
            Handle = handle;
        }

        /// <summary>
        /// create already ended bundle request for editor use
        /// </summary>
        /// <param name="loadedAsset"></param>
        public BundleAsyncRequest(T loadedAsset, TrackHandle<T> handle)
        {
            m_LoadedAsset = loadedAsset;
            Handle = handle;
        }

        //provide similar apis
        public override bool keepWaiting => m_Request == null ? false : !m_Request.isDone;
        public float Progress => m_Request == null ? 1f : m_Request.progress;
        public bool IsCompleted => m_Request == null ? true : m_Request.isDone;
        
        /// <summary>
        /// Actual loaded Asset.
        /// </summary>
        public T Asset => m_Request == null ? m_LoadedAsset : m_Request.asset as T;
        
        /// <summary>
        /// Release loaded asset. This is same with calling Release() function of TrackHandle.
        /// </summary>
        public void Dispose() => Handle.Release();

        BundleAsyncRequest<T> IAwaiter<BundleAsyncRequest<T>>.GetResult() => this;
        public IAwaiter<BundleAsyncRequest<T>> GetAwaiter() => this;

        /// <summary>
        /// Supress asset's auto release.
        /// </summary>
        public BundleAsyncRequest<T> Pin()
        {
            BundleManager.SupressAutoReleaseInternal(Handle.Id);
            return this;
        }

        void INotifyCompletion.OnCompleted(System.Action continuation)
        {
            if(Thread.CurrentThread.ManagedThreadId != BundleManager.UnityMainThreadId) 
            {
                throw new System.Exception("Should be awaited in UnityMainThread"); 
            }

            if(IsCompleted) continuation.Invoke();
            else m_Request.completed += op => continuation.Invoke();
        }

        void ICriticalNotifyCompletion.UnsafeOnCompleted(System.Action continuation) => ((INotifyCompletion)this).OnCompleted(continuation);
    }

    /// <summary>
    /// Async scene asset load request
    /// </summary>
    public class BundleAsyncSceneRequest : CustomYieldInstruction, IAwaiter<BundleAsyncSceneRequest>
    {
        internal readonly static BundleAsyncSceneRequest Failed = new BundleAsyncSceneRequest(null); 
        AsyncOperation m_AsyncOperation;

        /// <summary>
        /// The loaded scene, if it's not loaded or failed, this will be invalid scene.
        /// </summary>
        public Scene Scene { get; internal set; }

        internal BundleAsyncSceneRequest(AsyncOperation operation)
        {
            m_AsyncOperation = operation;
        }

        bool IAwaiter<BundleAsyncSceneRequest>.IsCompleted => !keepWaiting;

        public bool Succeeded => !keepWaiting && m_AsyncOperation != null;
        public override bool keepWaiting => m_AsyncOperation != null && !m_AsyncOperation.isDone;
        public float Progress =>  m_AsyncOperation != null? m_AsyncOperation.progress : 1f;

        BundleAsyncSceneRequest IAwaiter<BundleAsyncSceneRequest>.GetResult() => this;
        public IAwaiter<BundleAsyncSceneRequest> GetAwaiter() => this;

        void INotifyCompletion.OnCompleted(System.Action continuation)
        {
            if(Thread.CurrentThread.ManagedThreadId != BundleManager.UnityMainThreadId) 
            {
                throw new System.Exception("Should be awaited in UnityMainThread"); 
            }

            if(m_AsyncOperation.isDone) continuation.Invoke();
            else m_AsyncOperation.completed += op => continuation.Invoke();
        }

        void ICriticalNotifyCompletion.UnsafeOnCompleted(System.Action continuation) => ((INotifyCompletion)this).OnCompleted(continuation);
    }


    /// <summary>
    /// Download operation of BundleManager.
    /// </summary>
    public class BundleDonwloadAsyncOperation : BundleAsyncOperation, System.IDisposable
    {
        public bool IsDisposed { get; set; } = false;
        public bool WillBundleReplaced { get; internal set; } = false;
        public bool IsCancelled => ErrorCode == BundleErrorCode.UserCancelled;
        internal AssetBundleBuildManifest Manifest;
        internal HashSet<string> BundlesToUnload;
        internal List<LoadedBundle> BundlesToAddOrReplace;
        internal int Version;

        /// <summary>
        /// Try Cancel download operation.
        /// Note : it may result other resultcode if it's too late.
        /// </summary>
        public bool Cancel()
        {
            if(IsDone) 
            {
                if(BundleManager.LogMessages) Debug.LogError("The operation already ended!");
                return false;
            }
            Done(BundleErrorCode.UserCancelled);
            return true;
        }

        public bool Apply(bool unloadAllLoadedAssets = false, bool cleanUpCache = true)
            => ApplyInternal(false, unloadAllLoadedAssets, cleanUpCache);
        
        public bool ApplyAdditive(bool unloadAllLoadedAssets = false, bool cleanUpCache = true) 
            => ApplyInternal(true, unloadAllLoadedAssets, cleanUpCache);

        private bool ApplyInternal(bool additive, bool unload, bool cleanUp)
        {
            if(IsDisposed)
            {
                if(BundleManager.LogMessages) Debug.LogError("Can't apply disposed operation!");
                return false;
            }

            if(!Succeeded)
            {
                if(BundleManager.LogMessages) Debug.LogError("Can't apply not succeeded download!");
                return false;
            }

            return BundleManager.ApplyDownloadOperationInternal(this, additive, unload, cleanUp);
        }

        public void Dispose()
        {
            //already disposed
            if(IsDisposed) return;
            IsDisposed = true;
            
            //call cancel if it's not ended yet
            if(!IsDone) Cancel();
            BundleManager.CancelDownloadOperationInternal(this);
        }
    }

    /// <summary>
    /// Async operation of BundleManager. with result T
    /// </summary>
    public class BundleAsyncOperation<T> : BundleAsyncOperationBase, IAwaiter<BundleAsyncOperation<T>>
    {
        public T Result { get; internal set; }

        //awaiter implementations
        bool IAwaiter<BundleAsyncOperation<T>>.IsCompleted => IsDone;
        BundleAsyncOperation<T> IAwaiter<BundleAsyncOperation<T>>.GetResult() => this;
        public IAwaiter<BundleAsyncOperation<T>> GetAwaiter() => this;
        void INotifyCompletion.OnCompleted(System.Action continuation) => AwaiterOnComplete(continuation);
        void ICriticalNotifyCompletion.UnsafeOnCompleted(System.Action continuation) => AwaiterOnComplete(continuation);
    }

    /// <summary>
    /// Async operation of BundleManager.
    /// </summary>
    public class BundleAsyncOperation : BundleAsyncOperationBase, IAwaiter<BundleAsyncOperation>
    {
        //awaiter implementations
        bool IAwaiter<BundleAsyncOperation>.IsCompleted => IsDone;
        BundleAsyncOperation IAwaiter<BundleAsyncOperation>.GetResult() => this;
        public IAwaiter<BundleAsyncOperation> GetAwaiter() => this;
        void INotifyCompletion.OnCompleted(System.Action continuation) => AwaiterOnComplete(continuation);
        void ICriticalNotifyCompletion.UnsafeOnCompleted(System.Action continuation) => AwaiterOnComplete(continuation);
    }

    /// <summary>
    /// Base class of async bundle operation
    /// </summary>
    public class BundleAsyncOperationBase : CustomYieldInstruction
    {
        /// <summary>
        /// Whether this operation has completed or not.
        /// </summary>
        public bool IsDone => ErrorCode != BundleErrorCode.NotFinished;
        /// Whether this operation has succeeded or not.
        public bool Succeeded => ErrorCode == BundleErrorCode.Success;
        /// ErrorCode of the operation.
        public BundleErrorCode ErrorCode { get; private set; } = BundleErrorCode.NotFinished;
        /// <summary>
        /// Total Assetbundle Count to precess
        /// </summary>
        public int TotalCount { get; private set; } = 0;
        /// <summary>
        /// Precessed assetbundle count along with TotalCount
        /// -1 means processing has not been started.
        /// </summary>
        public int CurrentCount { get; private set; } = -1;
        /// <summary>
        /// Total progress of this operation. From 0 to 1.
        /// </summary>
        public float Progress { get; private set; } = 0f;
        /// <summary>
        /// Whether currently processing AssetBundle is loading from cache or downloading.
        /// </summary>
        public bool CurrentlyLoadingFromCache { get; private set; } = false;
        protected event System.Action m_OnComplete;
        /// <summary>
        /// Whether this operation has completed or not.
        /// </summary>
        public override bool keepWaiting => !IsDone;

        internal void SetCachedBundle(bool cached)
        {
            CurrentlyLoadingFromCache = cached;
        }

        internal void SetIndexLength(int total)
        {
            TotalCount = total;
        }

        internal void SetCurrentIndex(int current)
        {
            CurrentCount = current;
        }

        internal void SetProgress(float progress)
        {
            Progress = progress;
        }

        internal void Done(BundleErrorCode code)
        {
            if (code == BundleErrorCode.Success)
            {
                CurrentCount = TotalCount;
                Progress = 1f;
            }
            ErrorCode = code;
            m_OnComplete?.Invoke();
        }

        protected void AwaiterOnComplete(System.Action continuation)
        {
            if(Thread.CurrentThread.ManagedThreadId != BundleManager.UnityMainThreadId) 
            {
                throw new System.Exception("Should be awaited in UnityMainThread"); 
            }

            if(IsDone) continuation.Invoke();
            else m_OnComplete += continuation;
        }
    }

    /// <summary>
    /// Bundle Operation's ErrorCodes
    /// </summary>
    public enum BundleErrorCode
    {
        /// <summary>
        /// The operation is not yet finished.
        /// </summary>
        NotFinished = -1,
        /// <summary>
        /// The operation has been completed and succeeded.
        /// </summary>
        Success = 0,
        /// <summary>
        /// The BundleManager is not initialized thus the operation cannot be started.
        /// </summary>
        NotInitialized = 1,
        /// <summary>
        /// Faced a network related error during operation.
        /// </summary>
        NetworkError = 2,
        /// <summary>
        /// Unable to parse manifest.
        /// </summary>
        ManifestParseError = 3,
        /// <summary>
        /// User cancelled operation(download only)
        /// </summary>
        UserCancelled = 4,
    }
    
    /// <summary>
    /// Helper interface for async/await functionality
    /// </summary>
    public interface IAwaiter<out TResult> : ICriticalNotifyCompletion
    {
        bool IsCompleted { get; }

        TResult GetResult();

        IAwaiter<TResult> GetAwaiter();
    }
}