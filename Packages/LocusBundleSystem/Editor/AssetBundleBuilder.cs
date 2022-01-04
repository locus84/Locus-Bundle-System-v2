using System.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEngine;
using System;
using UnityEditor.Build.Pipeline.WriteTypes;

namespace BundleSystem
{
    /// <summary>
    /// class that contains assetbundle building functionalites
    /// </summary>
    public static class AssetBundleBuilder
    {
        const string LogFileName = "BundleBuildLog.txt";
        const string LogExpectedSharedBundleFileName = "ExpectedSharedBundles.txt";

        class CustomBuildParameters : BundleBuildParameters
        {
            Dictionary<string, BuildCompression> m_CompressSettings = new Dictionary<string, BuildCompression>();

            public CustomBuildParameters(
                List<BundleSetting> settings,
                AssetDependencyTree.ProcessResult result,
                BuildTarget target, 
                BuildTargetGroup group, 
                string outputPath) : base(target, group, outputPath)
            {
                //we use unity provided dependency result for final check
                var deps = result.BundleDependencies.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
                var localBundles = GetLcoalBundles(deps, settings);

                foreach(var setting in settings)
                {
                    var isLocal = localBundles.Contains(setting.BundleName);
                    var compressSetting = (isLocal || !setting.CompressBundle)? BuildCompression.LZ4 : BuildCompression.LZMA;
                    m_CompressSettings.Add(setting.BundleName, compressSetting);
                }
            }

            public override BuildCompression GetCompressionForIdentifier(string identifier) => m_CompressSettings[identifier];
        }

        /// <summary>
        /// Write list of shared assetbundles, this function is used for reducing unexpected shared bundles.
        /// The output file will be generated in the project root directory with name ExpectedSharedBundles.txt.
        /// </summary>
        /// <param name="setting">the setting you want to check shared bundle</param>
        public static void WriteExpectedSharedBundles(AssetBundleBuildSetting setting)
        {
            if(!Application.isBatchMode)
            {
                //have to ask save current scene
                var saved = UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

                if(!saved) 
                {
                    EditorUtility.DisplayDialog("Failed!", "User Canceled", "Confirm");
                    return;
                }
            }
            
            var bundleSettingList = setting.GetBundleSettings();
            var treeResult = AssetDependencyTree.ProcessDependencyTree(bundleSettingList);
            WriteSharedBundleLog($"{Application.dataPath}/../", treeResult);
            if(!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Succeeded!", $"Check {LogExpectedSharedBundleFileName} in your project root directory!", "Confirm");
            }
        }

        /// <summary>
        /// Build assetbundles with given setting.
        /// This refers input setting's output folder.
        /// </summary>
        /// <param name="setting">input setting</param>
        public static void BuildAssetBundles(AssetBundleBuildSetting setting)
        {
            if(!Application.isBatchMode)
            {
                //have to ask save current scene
                var saved = UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

                if(!saved) 
                {
                    EditorUtility.DisplayDialog("Build Failed!", $"User Canceled", "Confirm");
                    return;
                }
            }

            var bundleSettingList = setting.GetBundleSettings();
            var treeResult = AssetDependencyTree.ProcessDependencyTree(bundleSettingList);
            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            var groupTarget = BuildPipeline.GetBuildTargetGroup(buildTarget);//generate sharedBundle if needed, and pre generate dependency
            var buildParams = new CustomBuildParameters(bundleSettingList, treeResult, buildTarget, groupTarget, setting.OutputPath);
            buildParams.WriteLinkXML = true;
            buildParams.UseCache = !setting.ForceRebuild;
            var returnCode = ContentPipeline.BuildAssetBundles(buildParams, new BundleBuildContent(treeResult.ResultBundles.ToArray()), out var bundleResult);

            if (returnCode == ReturnCode.Success)
            {
                WriteManifestFile(buildParams.OutputFolder, setting, bundleResult, buildTarget, setting.RemoteURL);
                var linkPath = CopyLinkDotXml(setting.OutputPath, AssetDatabase.GetAssetPath(setting));
                if (!Application.isBatchMode) EditorUtility.DisplayDialog("Build Succeeded!", $"Bundle build succeeded, \n {linkPath} updated!", "Confirm");
            }
            else
            {
                EditorUtility.DisplayDialog("Build Failed!", $"Remote Bundle build failed, \n Code : {returnCode}", "Confirm");
                Debug.LogError(returnCode);
            }
        }

        static string CopyLinkDotXml(string outputPath, string settingPath)
        {
            var linkPath = Utility.CombinePath(outputPath, "link.xml");
            var movePath = Utility.CombinePath(settingPath.Remove(settingPath.LastIndexOf('/')), "link.xml");
            FileUtil.ReplaceFile(linkPath, movePath);
            AssetDatabase.Refresh();
            return linkPath;
        }

        /// <summary>
        /// write manifest into target path.
        /// </summary>
        static AssetBundleBuildManifest WriteManifestFile(string path, AssetBundleBuildSetting setting, IBundleBuildResults bundleResults, BuildTarget target, string remoteURL)
        {
            var manifest = new AssetBundleBuildManifest();
            manifest.BuildTarget = target.ToString();

            //we use unity provided dependency result for final check
            var deps = bundleResults.BundleInfos.ToDictionary(kv => kv.Key, kv => kv.Value.Dependencies.ToList());
            var locals = GetLcoalBundles(deps, setting.GetBundleSettings());

            foreach (var result in bundleResults.BundleInfos)
            {
                var bundleInfo = new AssetBundleBuildManifest.BundleInfo();
                bundleInfo.BundleName = result.Key;
                bundleInfo.Dependencies = Utility.CollectBundleDependencies(deps, result.Key);
                bundleInfo.HashString = result.Value.Hash.ToString();
                bundleInfo.IsLocal = locals.Contains(result.Key);
                bundleInfo.Size = new FileInfo(result.Value.FileName).Length;
                manifest.BundleInfos.Add(bundleInfo);
            }

            //sort by size
            manifest.BundleInfos.Sort((a, b) => b.Size.CompareTo(a.Size));
            var manifestString = JsonUtility.ToJson(manifest);
            manifest.GlobalHashString = Hash128.Compute(manifestString).ToString();
            manifest.BuildTime = DateTime.UtcNow.Ticks;
            manifest.DefaultRemoteURL = remoteURL;
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            File.WriteAllText(Utility.CombinePath(path, BundleManager.ManifestFileName), JsonUtility.ToJson(manifest, true));
            return manifest;
        }

        static void WriteSharedBundleLog(string path, AssetDependencyTree.ProcessResult treeResult)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Build Time : {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");
            sb.AppendLine($"Possible shared bundles will be created..");
            sb.AppendLine();

            var sharedBundleDic = treeResult.SharedBundles.ToDictionary(ab => ab.assetBundleName, ab => ab.assetNames[0]);

            //find flatten deps which contains non-shared bundles
            var definedBundles = treeResult.BundleDependencies.Keys.Where(name => !sharedBundleDic.ContainsKey(name)).ToList();
            var depsOnlyDefined = definedBundles.ToDictionary(name => name, name => Utility.CollectBundleDependencies(treeResult.BundleDependencies, name));

            foreach(var kv in sharedBundleDic)
            {
                var bundleName = kv.Key;
                var assetPath = kv.Value;
                var referencedDefinedBundles = depsOnlyDefined.Where(pair => pair.Value.Contains(bundleName)).Select(pair => pair.Key).ToList();

                sb.AppendLine($"Shared_{AssetDatabase.AssetPathToGUID(assetPath)} - { assetPath } is referenced by");
                foreach(var refBundleName in referencedDefinedBundles) sb.AppendLine($"    - {refBundleName}");
                sb.AppendLine();
            }

            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            File.WriteAllText(Utility.CombinePath(path, LogExpectedSharedBundleFileName), sb.ToString());
        }

        static List<string> GetLcoalBundles(Dictionary<string, List<string>> dependencies, List<BundleSetting> settings)
        {
            return settings.Where(bs => bs.IncludedInPlayer)
                .Select(bs => bs.BundleName)
                .SelectMany(bundleName => Utility.CollectBundleDependencies(dependencies, bundleName, true))
                .Distinct()
                .ToList();
        }
    }
}
