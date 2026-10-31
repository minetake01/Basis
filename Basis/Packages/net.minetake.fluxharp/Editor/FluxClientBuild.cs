using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Net.Minetake.FluxHarp.Editor
{
    /// <summary>
    /// Builds the Windows desktop/VR client with Flux Harp in it, from the command line:
    /// <c>-executeMethod Net.Minetake.FluxHarp.Editor.FluxClientBuild.BuildWindows -fluxBuildPath &lt;dir\Basis.exe&gt;</c>.
    /// Addressables are built first, as the client loads its menus and content through them.
    /// </summary>
    public static class FluxClientBuild
    {
        public static void BuildWindows()
        {
            string path = Argument("fluxBuildPath") ?? throw new BuildFailedException("-fluxBuildPath is required.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                {
                    throw new BuildFailedException("Could not switch to StandaloneWindows64.");
                }
            }
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;

            AddressableAssetSettings addressables = AddressableAssetSettingsDefaultObject.Settings
                ?? throw new BuildFailedException("Addressables settings are missing.");
            AddressableAssetSettings.BuildPlayerContent(out var content);
            if (!string.IsNullOrWhiteSpace(content.Error)) throw new BuildFailedException($"Addressables: {content.Error}");

            AddressableAssetSettings.PlayerBuildOption withPlayer = addressables.BuildAddressablesWithPlayerBuild;
            addressables.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
            try
            {
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = path,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    subtarget = (int)StandaloneBuildSubtarget.Player,
                    options = BuildOptions.None,
                });
                Debug.Log($"[FluxClientBuild] {report.summary.result} errors={report.summary.totalErrors} size={report.summary.totalSize} at {report.summary.outputPath}");
                if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException($"Player build {report.summary.result}.");
            }
            finally
            {
                addressables.BuildAddressablesWithPlayerBuild = withPlayer;
            }
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-" + name) return args[i + 1];
            }
            return null;
        }
    }
}
