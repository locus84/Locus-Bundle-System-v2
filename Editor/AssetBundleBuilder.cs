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
            Dictionary<string, bool> m_CompressSettings = new Dictionary<string, bool>();
            public bool IsLocalBundleBuilding { get; private set; }

            public CustomBuildParameters(List<BundleSetting> settings, 
                BuildTarget target, 
                BuildTargetGroup group) : base(target, group, "garbage")
            {
                foreach(var setting in settings)
                {
                    m_CompressSettings.Add(setting.BundleName, setting.CompressBundle);
                }
            }

            public override BuildCompression GetCompressionForIdentifier(string identifier)
            {
                //when building local bundles, we always use lzma
                if(IsLocalBundleBuilding) return BuildCompression.LZ4;
                var compress = !m_CompressSettings.TryGetValue(identifier, out var compressed) || compressed;
                return !compress ? BuildCompression.LZ4 : BuildCompression.LZMA;
            }

            public void ChangeSettings(string outputPath, bool isLocal, bool writeXml)
            {
                IsLocalBundleBuilding= isLocal;
                OutputFolder = outputPath;
                WriteLinkXML = writeXml;
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

            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            var groupTarget = BuildPipeline.GetBuildTargetGroup(buildTarget);//generate sharedBundle if needed, and pre generate dependency
            var buildParams = new CustomBuildParameters(bundleSettingList, buildTarget, groupTarget);
            var treeResult = AssetDependencyTree.ProcessDependencyTree(bundleSettingList);
            var bundleResult = default(IBundleBuildResults);
            var manifest = default(AssetBundleBuildManifest);
            var linkPath = default(string);
            buildParams.UseCache = !setting.ForceRebuild;


            //for remote build
            {
                buildParams.ChangeSettings(Utility.CombinePath(setting.OutputPath, buildTarget.ToString()), false, true);
                var returnCode = ContentPipeline.BuildAssetBundles(buildParams, new BundleBuildContent(treeResult.ResultBundles.ToArray()), out bundleResult);

                if (returnCode == ReturnCode.Success)
                {
                    manifest = WriteManifestFile(buildParams.OutputFolder, setting, bundleResult, buildTarget, setting.RemoteURL);
                    linkPath = CopyLinkDotXml(buildParams.OutputFolder, AssetDatabase.GetAssetPath(setting));
                }
                else
                {
                    EditorUtility.DisplayDialog("Build Failed!", $"Remote Bundle build failed, \n Code : {returnCode}", "Confirm");
                    Debug.LogError(returnCode);
                }
            }

            //for local build
            {
                buildParams.ChangeSettings(Utility.CombinePath(setting.LocalOutputPath, buildTarget.ToString()), true, false);
                var selectiveBuilder = new SelectiveBuilder(bundleResult, setting);
                ContentPipeline.BuildCallbacks.PostPackingCallback += selectiveBuilder.PostPackingForSelectiveBuild;
                var returnCode = ContentPipeline.BuildAssetBundles(buildParams, new BundleBuildContent(treeResult.ResultBundles.ToArray()), out bundleResult);
                ContentPipeline.BuildCallbacks.PostPackingCallback -= selectiveBuilder.PostPackingForSelectiveBuild;

                if (returnCode == ReturnCode.Success)
                {
                    WriteMergedManifest(buildParams.OutputFolder, manifest, bundleResult);
                    if (!Application.isBatchMode) EditorUtility.DisplayDialog("Build Succeeded!", $"Bundle build succeeded, \n {linkPath} updated!", "Confirm");
                }
                else
                {
                    EditorUtility.DisplayDialog("Build Failed!", $"Local build failed, \n Code : {returnCode}", "Confirm");
                    Debug.LogError(returnCode);
                }
            }
        }

        public class SelectiveBuilder
        {
            List<string> m_LocalBundles;
            public SelectiveBuilder(IBundleBuildResults previousResult, AssetBundleBuildSetting setting)
            {
                m_LocalBundles = GetLcoalBundles(previousResult, setting);
            }

            public ReturnCode PostPackingForSelectiveBuild(IBuildParameters buildParams, IDependencyData dependencyData, IWriteData writeData)
            {
                //quick exit 
                if (m_LocalBundles == null || m_LocalBundles.Count == 0)
                {
                    Debug.Log("Nothing to build");
                    writeData.WriteOperations.Clear();
                    return ReturnCode.Success;
                }

                for (int i = writeData.WriteOperations.Count - 1; i >= 0; --i)
                {
                    string bundleName;
                    switch (writeData.WriteOperations[i])
                    {
                        case SceneBundleWriteOperation sceneOperation:
                            bundleName = sceneOperation.Info.bundleName;
                            break;
                        case SceneDataWriteOperation sceneDataOperation:
                            var bundleWriteData = writeData as IBundleWriteData;
                            bundleName = bundleWriteData.FileToBundle[sceneDataOperation.Command.internalName];
                            break;
                        case AssetBundleWriteOperation assetBundleOperation:
                            bundleName = assetBundleOperation.Info.bundleName;
                            break;
                        default:
                            Debug.LogError("Unexpected write operation");
                            return ReturnCode.Error;
                    }

                    // if we do not want to build that bundle, remove the write operation from the list
                    if (!m_LocalBundles.Contains(bundleName))
                    {
                        writeData.WriteOperations.RemoveAt(i);
                    }
                }

                return ReturnCode.Success;
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

            var locals = GetLcoalBundles(bundleResults, setting);

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

        static void WriteMergedManifest(string path, AssetBundleBuildManifest manifest, IBundleBuildResults bundleResults)
        {
            foreach(var result in bundleResults.BundleInfos)
            {
                var pervInfoIndex = manifest.BundleInfos.FindIndex(bi => bi.BundleName == result.Key);
                var bundleInfo = manifest.BundleInfos[pervInfoIndex];
                bundleInfo.HashString = result.Value.Hash.ToString();
                bundleInfo.Size = new FileInfo(result.Value.FileName).Length;
                manifest.BundleInfos[pervInfoIndex] = bundleInfo;
            }

            //sort by size
            manifest.BundleInfos.Sort((a, b) => b.Size.CompareTo(a.Size));
            var manifestString = JsonUtility.ToJson(manifest);
            manifest.GlobalHashString = Hash128.Compute(manifestString).ToString();

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

        static List<string> GetLcoalBundles(IBundleBuildResults result, AssetBundleBuildSetting setting)
        {
            //we use unity provided dependency result for final check
            var deps = result.BundleInfos.ToDictionary(kv => kv.Key, kv => kv.Value.Dependencies.ToList());

            return setting.GetBundleSettings()
                .Where(bs => bs.IncludedInPlayer)
                .Select(bs => bs.BundleName)
                .SelectMany(bundleName => Utility.CollectBundleDependencies(deps, bundleName, true))
                .Distinct()
                .ToList();
        }
    }
}
