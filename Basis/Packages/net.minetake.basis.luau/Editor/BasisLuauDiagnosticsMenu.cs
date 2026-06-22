#if UNITY_EDITOR
using System.Text;
using Luau;
using Minetake.Basis.Luau.Runtime;
using UnityEditor;
using UnityEngine;

namespace Minetake.Basis.Luau.Editor
{
    public static class BasisLuauDiagnosticsMenu
    {
        [MenuItem("Tools/Basis Luau/Diagnose Native Update Reference")]
        public static void DiagnoseNativeUpdateReference()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[BasisLuau] Enter Play Mode to diagnose native update references.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("[BasisLuau] Diagnosing LuauFunctionRefHelper...");

            LuauScriptProxy[] proxies = Object.FindObjectsByType<LuauScriptProxy>(FindObjectsSortMode.None);
            if (proxies.Length == 0)
            {
                sb.AppendLine("No LuauScriptProxy instances found in the open scene.");
                Debug.Log(sb.ToString());
                return;
            }

            for (int i = 0; i < proxies.Length; i++)
            {
                LuauScriptProxy proxy = proxies[i];
                proxy.TryGetFunction("update", out LuauFunction update);
                int reference = LuauFunctionRefHelper.GetReference(update);
                sb.AppendLine(
                    $"Proxy '{proxy.name}': enabled={proxy.IsEnabled}, native={proxy.IsNativeRegistered}, " +
                    $"updateType={update?.GetType().Name ?? "null"}, registryRef={reference}");
            }

            Debug.Log(sb.ToString());
        }
    }
}
#endif
