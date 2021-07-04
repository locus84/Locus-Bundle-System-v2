﻿using System.Collections.Generic;
using UnityEditor;

namespace BundleSystem
{
    /// <summary>
    /// this class finds out duplicated topmost assets
    /// and make them into one single shared bundle one by one(to reduce bundle rebuild)
    /// so that there would be no asset duplicated
    /// </summary>
    public static class AssetDependencyTree
    {
        /// <summary>
        /// This class contains analyzed results of bundle reference tree.
        /// AssetBundle dependencies, actual AssetbundleBuild to pass SBP and shared bundles.
        /// </summary>
        public class ProcessResult
        {
            public Dictionary<string, HashSet<string>> BundleDependencies;
            public List<AssetBundleBuild> ResultBundles;
            public List<AssetBundleBuild> SharedBundles;
        }

        /// <summary>
        /// Analyze dependency tree of input bundle setting list.
        /// This function used when generating expected shared bundles, and when building actual assetbundles.
        /// </summary>
        /// <param name="bundleSettings"></param>
        /// <returns>Process result that contains all the results</returns>
        public static ProcessResult ProcessDependencyTree(List<BundleSetting> bundleSettings)
        {
            var context = new Context();
            var rootNodesToProcess = new List<RootNode>();
            var resultList = new List<AssetBundleBuild>();

            //collecting reference should be done after adding all root nodes
            //if not, there might be false positive shared bundle that already exist in bundle defines
            foreach (var setting in bundleSettings)
            {
                var bundle = new AssetBundleBuild();
                bundle.assetBundleName = setting.BundleName;
                bundle.assetNames = setting.AssetNames;
                bundle.addressableNames = setting.AddressableNames;
                resultList.Add(bundle);

                var depsHash = new HashSet<string>();
                context.DependencyDic.Add(bundle.assetBundleName, depsHash);
                foreach (var asset in bundle.assetNames)
                {
                    var rootNode = new RootNode(asset, bundle.assetBundleName, depsHash, false, setting.AutoSharedBundle);
                    context.RootNodes.Add(asset, rootNode);
                    rootNodesToProcess.Add(rootNode);
                }
            }

            //actually analize and create shared bundles
            foreach (var node in rootNodesToProcess)
            {
                if (!node.AllowCollect) continue;
                node.CollectNodes(context);
            }

            var sharedBundles = new List<AssetBundleBuild>();
            //convert found shared node proper struct
            foreach (var sharedRootNode in context.ResultSharedNodes)
            {
                var assetNames = new string[] { sharedRootNode.Path };
                var bundleDefinition = new AssetBundleBuild()
                {
                    assetBundleName = sharedRootNode.BundleName,
                    assetNames = assetNames,
                    addressableNames = assetNames
                };
                resultList.Add(bundleDefinition);
                sharedBundles.Add(bundleDefinition);
            }

            return new ProcessResult() { BundleDependencies = context.DependencyDic, ResultBundles = resultList, SharedBundles = sharedBundles };
        }

        /// <summary>
        /// class that holds informations while processing
        /// </summary>
        public class Context
        {
            public Dictionary<string, HashSet<string>> DependencyDic = new Dictionary<string, HashSet<string>>();
            public Dictionary<string, RootNode> RootNodes = new Dictionary<string, RootNode>();
            public Dictionary<string, Node> IndirectNodes = new Dictionary<string, Node>();
            public List<RootNode> ResultSharedNodes = new List<RootNode>();
        }

        /// <summary>
        /// Top-most asset reference. Usually entry of asset loading.
        /// </summary>
        public class RootNode : Node
        {
            public string BundleName { get; private set; }
            public bool IsShared { get; private set; }
            public bool AllowCollect { get; private set; }
            public HashSet<string> ReferencedBundleNames;


            public RootNode(string path, string bundleName, HashSet<string> deps, bool isShared, bool allowCollect) : base(path, null)
            {
                IsShared = isShared;
                BundleName = bundleName;
                AllowCollect = allowCollect;
                Root = this;
                ReferencedBundleNames = deps;
            }
        }

        /// <summary>
        /// Represents individual unity asset which presist as asset file.
        /// </summary>
        public class Node
        {
            public RootNode Root { get; protected set; }
            public string Path { get; private set; }
            public Dictionary<string, Node> Children = new Dictionary<string, Node>();
            public bool IsRoot => Root == this;
            public bool HasChild => Children.Count > 0;

            public Node(string path, RootNode root)
            {
                Root = root;
                Path = path;
            }

            public void RemoveFromTree(Context context)
            {
                context.IndirectNodes.Remove(Path);
                foreach (var kv in Children) kv.Value.RemoveFromTree(context);
            }

            public void CollectNodes(Context context)
            {
                var childDeps = AssetDatabase.GetDependencies(Path, false);

                //if it's a scene unwarp placed prefab directly into the scene
                if (Path.EndsWith(".unity")) childDeps = Utility.UnwarpSceneEncodedPrefabs(Path, childDeps);

                foreach (var child in childDeps)
                {
                    //is not bundled file
                    if (!Utility.IsAssetCanBundled(child)) continue;

                    //already root node, wont be included multiple times
                    if (context.RootNodes.TryGetValue(child, out var rootNode))
                    {
                        Root.ReferencedBundleNames.Add(rootNode.Root.BundleName);
                        continue;
                    }

                    //check if it's already indirect node (skip if it's same bundle)
                    //circular dependency will be blocked by indirect check
                    if (context.IndirectNodes.TryGetValue(child, out var node))
                    {
                        if (node.Root.BundleName != Root.BundleName)
                        {
                            node.RemoveFromTree(context);

                            var newName = $"Shared_{AssetDatabase.AssetPathToGUID(child)}";
                            var depsHash = new HashSet<string>();
                            context.DependencyDic.Add(newName, depsHash);
                            var newRoot = new RootNode(child, newName, depsHash, true, true);

                            //add deps
                            node.Root.ReferencedBundleNames.Add(newName);
                            Root.ReferencedBundleNames.Add(newName);

                            context.RootNodes.Add(child, newRoot);
                            context.ResultSharedNodes.Add(newRoot);
                            //is it okay to do here?
                            newRoot.CollectNodes(context);
                        }
                        continue;
                    }

                    //if not, add to indirect node
                    var childNode = new Node(child, Root);
                    context.IndirectNodes.Add(child, childNode);
                    Children.Add(child, childNode);
                    childNode.CollectNodes(context);
                }
            }
        }
    }
}
