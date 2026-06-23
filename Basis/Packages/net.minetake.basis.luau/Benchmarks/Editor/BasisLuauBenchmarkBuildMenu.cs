using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Minetake.Basis.Luau.Benchmarks.Editor
{
    public static class BasisLuauBenchmarkBuildMenu
    {
        const string ScenePath = "Assets/Temp/BasisLuauBenchmarkScene.unity";
        const string BuildDir = "Temp/BasisLuauBenchmarkPlayer";
        const string ExeName = "BasisLuauBenchmark.exe";
        const string NativeRuntimePath = "Packages/net.minetake.luau/Native/Plugins/win-x64/basis_luau_runtime.dll";

        [MenuItem("Tools/Basis Luau/Benchmarks/IL2CPP Player/Build")]
        public static void BuildBenchmarkPlayer() => BuildBenchmarkPlayer(runAfterBuild: false);

        [MenuItem("Tools/Basis Luau/Benchmarks/IL2CPP Player/Build and Run")]
        public static void BuildAndRunBenchmarkPlayer() => BuildBenchmarkPlayer(runAfterBuild: true);

        static void BuildBenchmarkPlayer(bool runAfterBuild)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            string previousScene = SceneManager.GetActiveScene().path;
            ScriptingImplementation previousBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            try
            {
                string scenePath = CreateBenchmarkScene();
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);

                Directory.CreateDirectory(BuildDir);
                string exePath = Path.GetFullPath(Path.Combine(BuildDir, ExeName));
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { scenePath },
                    locationPathName = exePath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                };

                BuildReportGuard(BuildPipeline.BuildPlayer(options));
                UnityEngine.Debug.Log($"[BasisLuau.Benchmark] Built IL2CPP benchmark player: {exePath}");

                if (runAfterBuild)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = Path.GetDirectoryName(exePath),
                        UseShellExecute = true,
                    });
                }
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, previousBackend);
                CleanupGeneratedScene();
                if (!string.IsNullOrEmpty(previousScene))
                {
                    EditorSceneManager.OpenScene(previousScene);
                }
            }
        }

        static string CreateBenchmarkScene()
        {
            Directory.CreateDirectory("Assets/Temp");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Basis Luau IL2CPP Benchmark Runner");
            BasisLuauBenchmarkRunner runner = root.AddComponent<BasisLuauBenchmarkRunner>();
            runner.Configure(
                BasisLuauBenchmarkProfile.Full,
                requireIl2Cpp: true,
                quitWhenComplete: true,
                gitCommit: ReadGitCommit(),
                runtimeHash: HashFile(NativeRuntimePath));

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.ImportAsset(ScenePath);
            return ScenePath;
        }

        static void CleanupGeneratedScene()
        {
            if (File.Exists(ScenePath))
            {
                AssetDatabase.DeleteAsset(ScenePath);
            }

            string metaPath = ScenePath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }

        static void BuildReportGuard(UnityEditor.Build.Reporting.BuildReport report)
        {
            UnityEditor.Build.Reporting.BuildSummary summary = report.summary;
            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Benchmark player build failed: {summary.result} ({summary.totalErrors} errors)");
            }
        }

        static string ReadGitCommit()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse --short=12 HEAD",
                    WorkingDirectory = Path.GetFullPath(".."),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using Process process = Process.Start(psi);
                if (process == null)
                {
                    return "unknown";
                }

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);
                return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        static string HashFile(string assetPath)
        {
            string path = Path.GetFullPath(assetPath);
            if (!File.Exists(path))
            {
                return "missing";
            }

            unchecked
            {
                ulong hash = 14695981039346656037UL;
                byte[] bytes = File.ReadAllBytes(path);
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= 1099511628211UL;
                }

                return hash.ToString("x16");
            }
        }
    }
}
