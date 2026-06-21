#if UNITY_EDITOR
using Minetake.Basis.Luau.Runtime;
using UnityEditor;
using UnityEngine;

namespace Minetake.Basis.Luau.Editor
{
    public static class LuauRuntimeBenchmarkMenu
    {
        [MenuItem("Basis/Luau/Run Runtime Micro-Benchmark")]
        public static void Run()
        {
            const int iterations = 10000;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                LuauCommandCatalog.IsRegistered(LuauCommandType.SetPosition);
            }

            sw.Stop();
            Debug.Log($"[BasisLuau] CommandCatalog hot path: {sw.ElapsedMilliseconds}ms / {iterations} iter");
        }
    }
}
#endif
