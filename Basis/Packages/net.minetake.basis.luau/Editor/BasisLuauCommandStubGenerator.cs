#if UNITY_EDITOR
using System.IO;
using System.Text;
using Minetake.Basis.Luau.Runtime;
using UnityEditor;
using UnityEngine;

namespace Minetake.Basis.Luau.Editor
{
    public static class BasisLuauCommandStubGenerator
    {
        [MenuItem("Basis/Luau/Generate Command Stub Catalog")]
        public static void Generate()
        {
            string dir = "Packages/net.minetake.basis.luau/Runtime/Generated";
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "LuauCommandStubCatalog.g.cs");
            var sb = new StringBuilder();
            sb.AppendLine("// Auto-generated deny-by-default command catalog references.");
            sb.AppendLine("namespace Minetake.Basis.Luau.Runtime.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    public static class LuauCommandStubCatalog");
            sb.AppendLine("    {");
            foreach (LuauCommandType type in System.Enum.GetValues(typeof(LuauCommandType)))
            {
                if (type == LuauCommandType.Invalid)
                {
                    continue;
                }

                sb.AppendLine($"        public const LuauCommandType {type} = LuauCommandType.{type};");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
            AssetDatabase.Refresh();
            Debug.Log($"[BasisLuau] Wrote {path}");
        }
    }
}
#endif
