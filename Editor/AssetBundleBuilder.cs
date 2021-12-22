using System.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEngine;
using System;

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
            Dictionary<string, bool> m_CompressSettings = new Dictionary<string, bool>();
            public bool IsLocalBundleBuilding { get; private set; }

            public CustomBuildParameters(List<BundleSetting> settings, 
                BuildTarget target, 
                BuildTargetGroup group, 
                string outputFolder,
                bool isLocalBundle) : base(target, group, outputFolder)
            {
                IsLocalBundleBuilding = isLocalBundle;
                foreach(var setting in settings)
                {
                    m_CompressSettings.Add(setting.BundleName, setting.CompressBundle);
                }
            }

            public override BuildCompression GetCompressionForIdentifier(string identifier)
            {
                //when building local bundles, we always use lzma
                if(IsLocalBundleBuilding) return BuildCompression.LZMA;
                var compress = !m_CompressSettings.TryGetValue(identifier, out var compressed) || compressed;
                return !compress ? BuildCompression.LZ4 : BuildCompression.LZMA;
            }
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
        public static void BuildAssetBundles(AssetBundleBuildSetting setting, bool localBundles = false)
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

            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            var groupTarget = BuildPipeline.GetBuildTargetGroup(buildTarget);

            var outputPath = Utility.CombinePath(localBundles? setting.LocalOutputPath : setting.OutputPath, buildTarget.ToString());
            //generate sharedBundle if needed, and pre generate dependency
            var treeResult = AssetDependencyTree.ProcessDependencyTree(bundleSettingList);
            
            var buildParams = new CustomBuildParameters(bundleSettingList, buildTarget, groupTarget, outputPath, localBundles);

            buildParams.UseCache = !setting.ForceRebuild;
            buildParams.WriteLinkXML = true;

            var returnCode = ContentPipeline.BuildAssetBundles(buildParams, new BundleBuildContent(treeResult.ResultBundles.ToArray()), out var results);

            if (returnCode == ReturnCode.Success)
            {
                WriteManifestFile(outputPath, setting, results, buildTarget, setting.RemoteURL);

                var linkPath = CopyLinkDotXml(outputPath, AssetDatabase.GetAssetPath(setting));
                if (!Application.isBatchMode) EditorUtility.DisplayDialog("Build Succeeded!", $"Remote bundle build succeeded, \n {linkPath} updated!", "Confirm");
            }
            else
            {
                EditorUtility.DisplayDialog("Build Failed!", $"Bundle build failed, \n Code : {returnCode}", "Confirm");
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
        static void WriteManifestFile(string path, AssetBundleBuildSetting setting, IBundleBuildResults bundleResults, BuildTarget target, string remoteURL)
        {
            var manifest = new AssetBundleBuildManifest();
            manifest.BuildTarget = target.ToString();

            //we use unity provided dependency result for final check
            var deps = bundleResults.BundleInfos.ToDictionary(kv => kv.Key, kv => kv.Value.Dependencies.ToList());

            var locals = setting.GetBundleSettings()
                .Where(bs => bs.IncludedInPlayer)
                .Select(bs => bs.BundleName)
                .SelectMany(bundleName => Utility.CollectBundleDependencies(deps, bundleName, true))
                .Distinct()
                .ToList();

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
            manifest.RemoteURL = remoteURL;
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            File.WriteAllText(Utility.CombinePath(path, BundleManager.ManifestFileName), JsonUtility.ToJson(manifest, true));
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
    }
}
