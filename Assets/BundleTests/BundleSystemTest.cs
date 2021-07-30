using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BundleSystem;
using UnityEditor;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Tests
{
    public class BundleSystemTest
    {
        AssetBundleBuildSetting m_PrevActiveSettingCache = null;
        Component m_Owner;

        [UnitySetUp]
        public IEnumerator InitializeTestSetup()
        {
            var downloadBundles = true;
#if UNITY_EDITOR
            //no setting
            if (!AssetBundleBuildSetting.TryGetActiveSetting(out var setting)) yield break;

            //load target test setting
            var toTest = AssetDatabase.LoadAssetAtPath<AssetBundleBuildSetting>("Assets/TestRemoteResources/AssetBundleBuildSetting.asset");

            if (toTest != setting)
            {
                //cache prev setting to recover
                m_PrevActiveSettingCache = setting;
                //make it active setting
                AssetBundleBuildSetting.SetActiveSetting(toTest, true);
                //do not download bundles if not emulation in editor
                downloadBundles = !BundleManager.UseAssetDatabaseMap;
            }
#endif
            //log messages
            BundleManager.LogMessages = true;

            //actual initialize function
            yield return BundleManager.Initialize();

            //skip remote bundle download test in build
            if (downloadBundles)
            {
                var manifestReq = BundleManager.GetManifest();
                yield return manifestReq;
                yield return BundleManager.DownloadAssetBundles(manifestReq.Result);
            }

            m_Owner = new GameObject("Owner").transform;
        }

        [TearDown]
        public void RestoreActiveSetting()
        {
#if UNITY_EDITOR
            if (m_PrevActiveSettingCache != null)
            {
                //restore setting
                AssetBundleBuildSetting.SetActiveSetting(m_PrevActiveSettingCache);
            }
#endif
            //when cleaning up, if m_owner 
            //has additional component inside a test, this will be re-created
            GameObject.Destroy(m_Owner.gameObject);
        }

        [UnityTest]
        public IEnumerator SyncApiTest()
        {
            //simple load
            var texReq = m_Owner.Load<Texture>("Local", "TestTexture_Local");
            Assert.NotNull(texReq.Asset);
            texReq.Dispose();
            BundleManager.UpdateImmediate();
            Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);

            //simple scene load, unload
            var scene = BundleManager.LoadScene("TestScene", UnityEngine.SceneManagement.LoadSceneMode.Additive);
            while(!scene.isLoaded) yield return null;
            Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 3);
            yield return SceneManager.UnloadSceneAsync(scene);
            BundleManager.UpdateImmediate();
            Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);

            //should be clean
            Assert.IsTrue(BundleManager.GetBundleReferenceSnapshot().Count == 0);
        }

        [UnityTest]
        public IEnumerator FailedHandleReleaseTest()
        {
            //sync pin empty
            {
                var texReq = m_Owner.Load<Texture>("Local", "Unknown");
                //try pin right after load
                texReq.Pin();
                BundleManager.UpdateImmediate();
                Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);
            }

            //sync dispose empty
            {
                var texReq = m_Owner.Load<Texture>("Local", "Unknown");
                //try dispose right after load loading
                texReq.Dispose();
                BundleManager.UpdateImmediate();
                Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);
            }

            //async pin empty
            {
                var texReq = m_Owner.LoadAsync<Texture>("Local", "Unknown");
                //try pin right after load
                texReq.Pin();
                yield return texReq;
                BundleManager.UpdateImmediate();
                Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);
            }

            //async dispose empty
            {
                var texReq = m_Owner.LoadAsync<Texture>("Local", "Unknown");
                //try dispose while loading
                texReq.Dispose();
                //wait loading complete event after dispose(is possible actually)
                yield return texReq;
                BundleManager.UpdateImmediate();
                Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);
            }

            //should be clean
            Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);
        }

        [UnityTest]
        public IEnumerator AsyncApiTest()
        {
            var go = new GameObject("Go");
            var image = go.AddComponent<UnityEngine.UI.Image>();
            var spriteReq = image.LoadAsync<Sprite>("Object", "TestSprite");
            yield return spriteReq;
            image.sprite = spriteReq.Pin().Asset;
            var handle = default(TrackHandle<Sprite>);
            spriteReq.Handle.Override(ref handle);
            BundleManager.UpdateImmediate();
            Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 1);
            handle.Release();
            BundleManager.UpdateImmediate();
            Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);
            Assert.IsTrue(BundleManager.GetBundleReferenceSnapshot().Count == 0);
        }

        [UnityTest]
        public IEnumerator SceneApiTest()
        {
            //scene load test
            {
                var loadReq1 = BundleManager.LoadSceneAsync("TestScene", UnityEngine.SceneManagement.LoadSceneMode.Additive);
                var loadReq3 = BundleManager.LoadSceneAsync("TestScene", UnityEngine.SceneManagement.LoadSceneMode.Additive);
                yield return loadReq1;
                yield return loadReq3;
                Assert.IsTrue(loadReq1.Scene.IsValid() && loadReq1.Succeeded);

                //load subscene which contains two gameobjects
                var loadReq2 = BundleManager.LoadSceneAsync("TestScene_SubDir", UnityEngine.SceneManagement.LoadSceneMode.Additive);
                yield return loadReq2;

                //allow error message
                LogAssert.ignoreFailingMessages = true;
                var loadReq4 = BundleManager.LoadSceneAsync("TestSceneUnknown", UnityEngine.SceneManagement.LoadSceneMode.Additive);
                yield return loadReq4;
                Assert.IsTrue(!loadReq4.Scene.IsValid() && !loadReq4.Succeeded);
                LogAssert.ignoreFailingMessages = false;

                //there are two same scene loaded with 3 objects + 2 for subscene -> 3 * 2 + 2 = 8
                Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 8);

                //unload two scene (total 5 objects)
                yield return SceneManager.UnloadSceneAsync(loadReq1.Scene);
                yield return SceneManager.UnloadSceneAsync(loadReq2.Scene);

                //letmanager update destryed
                BundleManager.UpdateImmediate();
                Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 3);

                //check the scene objects are propery pinned
                yield return new WaitForSecondsRealtime(2);
                Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 3);
            }

            //the game objects must be there(in TestScene)
            var parentGo = GameObject.Find("TestSceneObject");
            var childGo = GameObject.Find("TestSceneObjectChild");

            Assert.NotNull(parentGo);
            Assert.NotNull(childGo);

            //track handle searches parent object
            Assert.IsTrue(childGo.GetInstanceTrackHandle().IsValid());
            //so handle id must be same                            
            Assert.IsTrue(childGo.GetInstanceTrackHandle().Id == parentGo.GetInstanceTrackHandle().Id);

            var childHandle = parentGo.GetInstanceTrackHandle().TrackInstanceExplicit(childGo);
            Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 4); // added child handle

            Assert.IsTrue(childHandle.IsValid());
            Assert.IsTrue(childHandle.Id == childGo.GetInstanceTrackHandle().Id);
            Assert.IsTrue(childHandle.Id != parentGo.GetInstanceTrackHandle().Id);

            //after unload(destroyed) track cound should be zero
            yield return SceneManager.UnloadSceneAsync("TestScene");
            BundleManager.UpdateImmediate();
            Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);
            Assert.IsTrue(BundleManager.GetBundleReferenceSnapshot().Count == 0);
        }

        [UnityTest]
        public IEnumerator PinTest()
        {
            //auto release
            {
                var spriteReq = m_Owner.LoadAsync<Sprite>("Object", "TestSprite");
                yield return spriteReq;
                Assert.NotNull(spriteReq.Asset);
                yield return new WaitForSecondsRealtime(2);

                //after two seconds, it must be auto-released
                Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);
            }

            //pin request
            {
                var spriteReq = m_Owner.LoadAsync<Sprite>("Object", "TestSprite");
                yield return spriteReq;
                Assert.NotNull(spriteReq.Pin().Asset);
                yield return new WaitForSecondsRealtime(2);

                //it must not be auto-released as it's pinned 
                Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 1);
                spriteReq.Dispose();
                BundleManager.UpdateImmediate();
                Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);
            }

            //dispose
            {
                var spriteReq = m_Owner.LoadAsync<Sprite>("Object", "TestSprite");
                yield return spriteReq;
                Assert.NotNull(spriteReq.Asset);

                //dispose function releases handle so it should be released
                spriteReq.Dispose();
                BundleManager.UpdateImmediate();
                Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);
            }

            //handle release
            {
                var spriteReq = m_Owner.LoadAsync<Sprite>("Object", "TestSprite");
                yield return spriteReq;
                Assert.NotNull(spriteReq.Asset);

                //release function explicitely release a handle
                spriteReq.Handle.Release();
                BundleManager.UpdateImmediate();
                Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);
            }

            //should be clean
            Assert.IsTrue(BundleManager.GetBundleReferenceSnapshot().Count == 0);
        }

        [UnityTest]
        public IEnumerator TaskAsyncApiTest()
        {
            var task = TaskAsyncApiTestFunction();
            while (!task.IsCompleted) yield return null;
        }

        private async Task TaskAsyncApiTestFunction()
        {
            //simple async function
            var go = new GameObject("Go");
            var image = go.AddComponent<UnityEngine.UI.Image>();
            var req = await image.LoadAsync<Sprite>("Object", "TestSprite");
            image.sprite = req.Pin().Asset;
            var prevHandle = req.Handle;

            //load second and override existing handle
            var otherReq = await image.LoadAsync<Sprite>("Object", "TestSprite");
            image.sprite = otherReq.Pin().Asset;
            otherReq.Handle.Override(ref prevHandle);
            BundleManager.UpdateImmediate();
            Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 1);

            //it's overriden and pointing new handle
            prevHandle.Release(); 
            BundleManager.UpdateImmediate();
            Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);

            //should be clean
            Assert.IsTrue(BundleManager.GetBundleReferenceSnapshot().Count == 0);
        }
    }
}
