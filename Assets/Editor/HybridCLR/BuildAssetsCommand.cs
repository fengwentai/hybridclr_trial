﻿using HybridCLR.Editor.Commands;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Main;
using UnityEditor;
using UnityEngine;

namespace HybridCLR.Editor
{
    public static class BuildAssetsCommand
    {
        public static string AssetBundleOutputDir => Application.dataPath + "/../AssetBundles";

        public static string GetAssetBundleOutputDirByTarget(BuildTarget target)
        {
            return $"{AssetBundleOutputDir}/{target}";
        }

        public static string ToRelativeAssetPath(string s)
        {
            return s.Substring(s.IndexOf("Assets/"));
        }

        /// <summary>
        /// Build Prefab 到Asstbundle包
        /// </summary>
        /// <param name="outputDir"></param>
        /// <param name="target"></param>
        private static void BuildAssetBundles(string outputDir, BuildTarget target)
        {
            Directory.CreateDirectory(outputDir);

            List<AssetBundleBuild> abs = new List<AssetBundleBuild>();

            {
                var prefabAssets = new List<string>();
                string testPrefab = $"{Application.dataPath}/Prefabs/HotUpdatePrefab.prefab";
                prefabAssets.Add(testPrefab);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                abs.Add(new AssetBundleBuild
                {
                    assetBundleName = "prefabs",
                    assetNames = prefabAssets.Select(s => ToRelativeAssetPath(s)).ToArray(),
                });
            }

            BuildPipeline.BuildAssetBundles(outputDir, abs.ToArray(), BuildAssetBundleOptions.None, target);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        public static void BuildAssetBundleByTarget(BuildTarget target)
        {
            BuildAssetBundles(GetAssetBundleOutputDirByTarget(target), target);
        }

        // [MenuItem("HybridCLR/Build/BuildAssetBundle")]
        public static void BuildSceneAssetBundleActiveBuildTargetExcludeAOT()
        {
            BuildAssetBundleByTarget(EditorUserBuildSettings.activeBuildTarget);
        }

        [MenuItem("HybridCLR/Build/BuildAssetsAndDll")]
        public static void BuildAndCopyABAOTHotUpdateDlls()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            BuildAssetBundleByTarget(target);
            CompileDllCommand.CompileDll(target);
            CopyABAOTHotUpdateDlls(target);
        }

        [MenuItem("HybridCLR/Build/CopyToStreamingAssets")]
        public static void CopyABAOTHotUpdateDlls()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            var srcPath = AssetUtility.FullPathToAssetPath(GetAssetBundleOutputDirByTarget(target));
            var dstPath = AssetUtility.FullPathToAssetPath(AssetUtility.GetStreamingAssetsDataPath());
            AssetDatabase.DeleteAsset(dstPath);
            AssetDatabase.Refresh();
            
            try
            {
                FileUtil.CopyFileOrDirectoryFollowSymlinks(srcPath, dstPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("Something wrong, you need manual delete AssetBundles folder in StreamingAssets, err : " + ex);
                return;
            }

            var allManifest = AssetUtility.GetSpecifyFilesInFolder(dstPath, new string[] { ".manifest" });
            if (allManifest != null && allManifest.Length > 0)
            {
                for (int i = 0; i < allManifest.Length; i++)
                {
                    AssetUtility.SafeDeleteFile(allManifest[i]);
                }
            }
            
            AssetDatabase.Refresh();
        }
        
        [MenuItem("HybridCLR/Build/OpenPersistentPath")]
        public static void OpenPersistentPath()
        {
            AssetUtility.ExplorerFolder(Application.persistentDataPath);
        }
        
        public static void CopyABAOTHotUpdateDlls(BuildTarget target)
        {
            CopyAssetBundlesToStreamingAssets(target);
            CopyAOTAssembliesToStreamingAssets();
            CopyHotUpdateAssembliesToStreamingAssets();

            GenVersionFiles();
        }

        static void GenVersionFiles()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            var path = GetAssetBundleOutputDirByTarget(target) + '\\';
            string outFile = path + "/Versions.txt";
            if (File.Exists(outFile)) File.Delete(outFile);

            var files = Directory.GetFiles(path);
            var sb = new StringBuilder();
            foreach (var file in files)
            {
                if (!file.EndsWith(".manifest"))
                {
                    sb.AppendLine(file.Replace(path, "") + "," + AssetUtility.GetMD5(file));
                }
            }

            File.WriteAllText(outFile, sb.ToString());
        }

        public static void CopyAOTAssembliesToStreamingAssets()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            string aotAssembliesSrcDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            string aotAssembliesDstDir = GetAssetBundleOutputDirByTarget(target);
            ;

            foreach (var dll in LoadDll.AOTMetaAssemblyNames)
            {
                string srcDllPath = $"{aotAssembliesSrcDir}/{dll}";
                if (!File.Exists(srcDllPath))
                {
                    Debug.LogError(
                        $"ab中添加AOT补充元数据dll:{srcDllPath} 时发生错误,文件不存在。裁剪后的AOT dll在BuildPlayer时才能生成，因此需要你先构建一次游戏App后再打包。");
                    continue;
                }

                string dllBytesPath = $"{aotAssembliesDstDir}/{dll}.bytes";
                File.Copy(srcDllPath, dllBytesPath, true);
                Debug.Log($"[CopyAOTAssembliesToStreamingAssets] copy AOT dll {srcDllPath} -> {dllBytesPath}");
            }
        }

        public static void CopyHotUpdateAssembliesToStreamingAssets()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;

            string hotfixDllSrcDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
            string hotfixAssembliesDstDir = GetAssetBundleOutputDirByTarget(target);
#if NEW_HYBRIDCLR_API
            foreach (var dll in SettingsUtil.HotUpdateAssemblyFilesExcludePreserved)
#else
            foreach (var dll in SettingsUtil.HotUpdateAssemblyFiles)
#endif
            {
                string dllPath = $"{hotfixDllSrcDir}/{dll}";
                string dllBytesPath = $"{hotfixAssembliesDstDir}/{dll}.bytes";
                File.Copy(dllPath, dllBytesPath, true);
                Debug.Log($"[CopyHotUpdateAssembliesToStreamingAssets] copy hotfix dll {dllPath} -> {dllBytesPath}");
            }
        }

        public static void CopyAssetBundlesToStreamingAssets(BuildTarget target)
        {
            string streamingAssetPathDst = Application.streamingAssetsPath;
            Directory.CreateDirectory(streamingAssetPathDst);
            string outputDir = GetAssetBundleOutputDirByTarget(target);
            var abs = new string[] { "prefabs", "AssetBundles" };
            foreach (var ab in abs)
            {
                string srcAb = ToRelativeAssetPath($"{outputDir}/{ab}");
                string dstAb = ToRelativeAssetPath($"{streamingAssetPathDst}/{ab}");
                Debug.Log($"[CopyAssetBundlesToStreamingAssets] copy assetbundle {srcAb} -> {dstAb}");
                AssetDatabase.CopyAsset(srcAb, dstAb);
            }
        }
    }
}