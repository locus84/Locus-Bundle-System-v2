using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BundleSystem;
using UnityEditor;
using System.Threading.Tasks;

namespace Tests
{
    public class BundleSystemTest
    {
        AssetBundleBuildSetting m_PrevActiveSettingCache = null;
        Component m_Owner;

        [UnitySetUp]
        public IEnumerator InitializeTestSetup()
        {
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

            //log messages
            BundleManager.LogMessages = true;
            BundleManager.ShowDebugGUI = true;

            //actual initialize function
            yield return BundleManager.Initialize();
            var manifestReq = BundleManager.GetManifest();

            yield return manifestReq;
            yield return BundleManager.DownloadAssetBundles(manifestReq.Result);

            m_Owner = new GameObject("Owner").transform;
        }

        [TearDown]
        public void RestoreActiveSetting()
        {
            if(m_PrevActiveSettingCache != null)
            {
                //restore setting
                AssetBundleBuildSetting.SetActiveSetting(m_PrevActiveSettingCache);
            }
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
