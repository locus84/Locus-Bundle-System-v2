﻿using BundleSystem;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SampleTitleScene : MonoBehaviour
{
    List<GameObject> m_Instances = new List<GameObject>();

    public void DownloadAssetBundles()
    {
        StartCoroutine(CoDownloadAssetBundles());
    }

    IEnumerator CoDownloadAssetBundles()
    {
        var manifestReq = BundleManager.GetManifest();
        yield return manifestReq;
        if (!manifestReq.Succeeded)
        {
            Debug.LogError(manifestReq.ErrorCode);
            yield break;
        }
        var downloadReq = BundleManager.DownloadAssetBundles(manifestReq.Result);
        yield return downloadReq;
        if (!downloadReq.Succeeded)
        {
            Debug.LogError(downloadReq.ErrorCode);
            yield break;
        }
    }

    // Start is called before the first frame update
    public void OnSpawnButton()
    {
        var loadedAsset = BundleManager.Load<GameObject>("Remote", "Cube");
        if(loadedAsset != null)
        {
            m_Instances.Add(BundleManager.Instantiate(loadedAsset));
            BundleManager.ReleaseObject(loadedAsset);
        }
    }

    // Start is called before the first frame update
    public void DestroyAllButton()
    {
        foreach (var go in m_Instances) Destroy(go);
        m_Instances.Clear();
    }
}
