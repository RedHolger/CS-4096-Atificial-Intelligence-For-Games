using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orchard.EditorTools
{
    public static class OrchardBuild
    {
        public const string ScenePath = "Assets/Orchard/Scenes/Main.unity";

        [MenuItem("Orchard/Prepare Scene & Settings")]
        public static void PrepareProject()
        {
            string sceneDir = Path.GetDirectoryName(ScenePath);
            if (!string.IsNullOrEmpty(sceneDir) && !Directory.Exists(sceneDir))
                Directory.CreateDirectory(sceneDir);

            if (!File.Exists(ScenePath))
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.Refresh();
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.companyName = "CS4096";
            PlayerSettings.productName = "Orchard of the Last Season";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.cs4096.orchardofthelastseason");
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 1252;
            PlayerSettings.defaultScreenHeight = 704;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.macRetinaSupport = true;

            AssetDatabase.SaveAssets();
            Debug.Log("Orchard: prepared Main.unity scene and standalone player settings.");
        }

        [MenuItem("Orchard/Build macOS App")]
        public static void BuildMacOS()
        {
            string repoRoot = Directory.GetParent(Application.dataPath).Parent.FullName;
            string outPath = Path.Combine(repoRoot, "Builds", "macOS", "OrchardOfTheLastSeason.app");
            BuildForTarget(BuildTarget.StandaloneOSX, outPath);
        }

        public static void BuildFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string targetArg = GetArg(args, "-orchardTarget") ?? "StandaloneOSX";
            string outPath = GetArg(args, "-orchardBuildPath");

            BuildTarget target = ParseTarget(targetArg);
            if (string.IsNullOrEmpty(outPath))
            {
                string repoRoot = Directory.GetParent(Application.dataPath).Parent.FullName;
                outPath = DefaultOutputPath(repoRoot, target);
            }

            bool ok = BuildForTarget(target, outPath);
            if (Application.isBatchMode)
                EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool BuildForTarget(BuildTarget target, string outPath)
        {
            PrepareProject();

            string parentDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                Directory.CreateDirectory(parentDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outPath,
                target = target,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Orchard: build succeeded ({summary.platform}) -> {outPath} ({summary.totalSize / (1024f * 1024f):F1} MB, {summary.totalTime.TotalSeconds:F1}s)");
                return true;
            }

            Debug.LogError($"Orchard: build failed ({summary.result}) with {summary.totalErrors} error(s).");
            return false;
        }

        static BuildTarget ParseTarget(string raw)
        {
            switch (raw.ToLowerInvariant())
            {
                case "macos":
                case "osx":
                case "standaloneosx":
                    return BuildTarget.StandaloneOSX;
                case "win64":
                case "windows":
                case "standalonewindows64":
                    return BuildTarget.StandaloneWindows64;
                case "linux":
                case "linux64":
                case "standalonelinux64":
                    return BuildTarget.StandaloneLinux64;
                case "webgl":
                    return BuildTarget.WebGL;
                default:
                    throw new ArgumentException("Unsupported -orchardTarget: " + raw);
            }
        }

        static string DefaultOutputPath(string repoRoot, BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneOSX:
                    return Path.Combine(repoRoot, "Builds", "macOS", "OrchardOfTheLastSeason.app");
                case BuildTarget.StandaloneWindows64:
                    return Path.Combine(repoRoot, "Builds", "Windows", "OrchardOfTheLastSeason.exe");
                case BuildTarget.StandaloneLinux64:
                    return Path.Combine(repoRoot, "Builds", "Linux", "OrchardOfTheLastSeason.x86_64");
                case BuildTarget.WebGL:
                    return Path.Combine(repoRoot, "Builds", "WebGL");
                default:
                    return Path.Combine(repoRoot, "Builds", target.ToString(), "OrchardOfTheLastSeason");
            }
        }

        static string GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }
    }
}
