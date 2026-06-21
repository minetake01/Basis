#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Minetake.Basis.Luau.Editor
{
    static class BasisLuauNativeDeployer
    {
        const string RuntimeDllName = "basis_luau_runtime.dll";

        [InitializeOnLoadMethod]
        static void DeployOnLoad()
        {
            TryDeploy(showSuccessLog: false);
        }

        [MenuItem("Tools/Basis Luau/Deploy Native Runtime")]
        static void DeployFromMenu()
        {
            if (TryDeploy(showSuccessLog: true))
            {
                EditorUtility.DisplayDialog(
                    "Basis Luau",
                    "Native runtime DLL deployed.\n\nRestart the Unity Editor if Play Mode still reports bytecode verifier errors.",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Basis Luau",
                    "Could not deploy the native runtime DLL because the file is locked.\n\nClose the Unity Editor, run Native~/deploy-built.ps1, then reopen the project.",
                    "OK");
            }
        }

        static bool TryDeploy(bool showSuccessLog)
        {
            string pluginsDir = Path.GetFullPath(Path.Combine("Packages", "net.minetake.luau", "Native", "Plugins", "win-x64"));
            string builtPath = Path.Combine(pluginsDir, RuntimeDllName + ".built");
            string destPath = Path.Combine(pluginsDir, RuntimeDllName);

            if (!File.Exists(builtPath))
            {
                return false;
            }

            var builtInfo = new FileInfo(builtPath);
            if (File.Exists(destPath))
            {
                var destInfo = new FileInfo(destPath);
                if (builtInfo.Length == destInfo.Length && builtInfo.LastWriteTimeUtc <= destInfo.LastWriteTimeUtc)
                {
                    return true;
                }
            }

            try
            {
                File.Copy(builtPath, destPath, true);
                AssetDatabase.ImportAsset(Path.Combine("Packages", "net.minetake.luau", "Native", "Plugins", "win-x64", RuntimeDllName));
                if (showSuccessLog)
                {
                    Debug.Log($"[BasisLuau] Deployed {RuntimeDllName} from .built ({builtInfo.Length} bytes).");
                }
                return true;
            }
            catch (IOException ex)
            {
                Debug.LogWarning($"[BasisLuau] Could not deploy {RuntimeDllName}: {ex.Message}. Restart Unity after closing the editor.");
                return false;
            }
        }
    }
}
#endif
