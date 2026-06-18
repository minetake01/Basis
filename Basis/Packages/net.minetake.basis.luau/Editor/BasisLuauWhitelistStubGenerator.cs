#if UNITY_EDITOR
using System.IO;
using System.Text;
using Minetake.Basis.Luau.Policy;
using UnityEditor;
using UnityEngine;

namespace Minetake.Basis.Luau.Editor
{
    public static class BasisLuauWhitelistStubGenerator
    {
        [MenuItem("Basis/Luau/Generate Whitelist Stubs")]
        public static void Generate()
        {
            string path = "Packages/net.minetake.basis.luau/Samples/Luau/basis_whitelist_stubs.luau";
            var sb = new StringBuilder();
            sb.AppendLine("-- Auto-generated type name reference for Luau authors");
            sb.AppendLine("return {");
            AppendTypes(sb, LuauPropWhitelistPolicy.Instance);
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
            AssetDatabase.Refresh();
            Debug.Log($"[BasisLuau] Wrote {path}");
        }

        static void AppendTypes(StringBuilder sb, LuauWhitelistPolicy policy)
        {
            string[] samples =
            {
                "TMPro.TMP_Text",
                "UnityEngine.UI.Button",
                "UnityEngine.AudioSource",
                "UnityEngine.Rigidbody",
                "UnityEngine.AI.NavMesh",
            };

            for (int i = 0; i < samples.Length; i++)
            {
                string t = samples[i];
                bool allowed = policy.CheckTypeAllowed(t);
                sb.AppendLine($"  [\"{t}\"] = {allowed.ToString().ToLowerInvariant()},");
            }
        }
    }
}
#endif
