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
#if UNITY_EDITOR
            //no setting
            if(!AssetBundleBuildSetting.TryGetActiveSetting(out var setting)) yield break;

            //load target test setting
            var toTest = AssetDatabase.LoadAssetAtPath<AssetBundleBuildSetting>("Assets/TestRemoteResources/AssetBundleBuildSetting.asset");

            if(toTest != setting)
            {
                //cache prev setting to recover
                m_PrevActiveSettingCache = setting;
                //make it active setting
                AssetBundleBuildSetting.SetActiveSetting(toTest, true);
            }
#endif
            //log messages
            BundleManager.LogMessages = true;
            BundleManager.ShowDebugGUI = true;

            //actual initialize function
            yield return BundleManager.Initialize();

            //skip remote bundle download test in build
            if(Application.isEditor)
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
            if(m_PrevActiveSettingCache != null)
            {
                //restore setting
                AssetBundleBuildSetting.SetActiveSetting(m_PrevActiveSettingCache);
            }
#endif
        }

        [Test]
        public void SyncApiTest()
        {
            var texReq = m_Owner.Load<Texture>("Local", "TestTexture_Local");

            Assert.NotNull(texReq.Asset);
        }

        UnityEngine.UI.Image m_Image;

        TrackHandle<Sprite> m_SpriteHandle;

        [UnityTest]
        public IEnumerator AsyncApiTest()
        {
            m_Image = m_Owner.gameObject.AddComponent<UnityEngine.UI.Image>();
            var spriteReq = m_Image.LoadAsync<Sprite>("Object", "TestSprite");
            //disabled
            yield return spriteReq;
            m_Image.sprite = spriteReq.Pin().Asset;
            spriteReq.Handle.Override(ref m_SpriteHandle);
            BundleManager.UpdateImmediate();
            Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 1);
            m_SpriteHandle.Release();
            BundleManager.UpdateImmediate();
            Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);
        }

        [UnityTest]
        public IEnumerator SceneApiTest()
        {
            var loadReq = BundleManager.LoadSceneAsync("Scene", "TestScene", UnityEngine.SceneManagement.LoadSceneMode.Additive);
            yield return loadReq;

            //the scene have 3 root game object
            Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 3);

            yield return new WaitForSecondsRealtime(2); 
            Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 3); // does not auto released (pinned)

            //the game objects must be there
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
                Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);
            }

            //pin request
            {
                var spriteReq = m_Owner.LoadAsync<Sprite>("Object", "TestSprite");
                yield return spriteReq;
                Assert.NotNull(spriteReq.Pin().Asset);
                yield return new WaitForSecondsRealtime(2);
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
                spriteReq.Dispose();
                BundleManager.UpdateImmediate();
                Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);
            }
            
            //handle release
            {
                var spriteReq = m_Owner.LoadAsync<Sprite>("Object", "TestSprite");
                yield return spriteReq;
                Assert.NotNull(spriteReq.Asset);
                spriteReq.Handle.Release();
                BundleManager.UpdateImmediate();
                Assert.IsTrue(BundleManager.GetTrackingSnapshot().Count == 0);
            }
        }


        [UnityTest]
        public IEnumerator TaskAsyncApiTest()
        {
            var task = TaskAsyncApiTestFunction();
            while(!task.IsCompleted) yield return null;
        }

        private async Task TaskAsyncApiTestFunction()
        {
            var req = await m_Owner.LoadAsync<Sprite>("Object", "TestSprite");
            m_Image.sprite = req.Asset;
            req.Handle.Override(ref m_SpriteHandle);
        }
    }
}
