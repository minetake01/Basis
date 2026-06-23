#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Minetake.Basis.Luau.Editor
{
    public static class LuauRuntimeBenchmarkMenu
    {
        static readonly string[] CoreBenchmarkTestNames =
        {
            "Minetake.Basis.Luau.Tests.LuauUnityBenchmarkTests.CommandCatalog_IsRegistered",
            "Minetake.Basis.Luau.Tests.LuauUnityBenchmarkTests.AuthorityTable_RegisterAndResolve",
            "Minetake.Basis.Luau.Tests.LuauUnityBenchmarkTests.AuthorityTable_ManyTransformSnapshots",
            "Minetake.Basis.Luau.Tests.LuauUnityBenchmarkTests.CommandFlush_SetPosition",
            "Minetake.Basis.Luau.Tests.LuauBridgeBenchmarkTests.CommandRing_PushPopThroughput",
            "Minetake.Basis.Luau.Tests.LuauBridgeBenchmarkTests.SnapshotPublish_WithTransformSlots",
            "Minetake.Basis.Luau.Tests.LuauBridgeBenchmarkTests.FullPumpUpdate_NoProxies",
            "Minetake.Basis.Luau.Tests.LuauBridgeBenchmarkTests.FullPumpUpdate_WithCommandBacklog",
            "Minetake.Basis.Luau.Tests.LuauScriptBenchmarkTests.LuauUpdate_MinimalSingleProxy",
            "Minetake.Basis.Luau.Tests.LuauScriptBenchmarkTests.LuauUpdate_ComputeSingleProxy",
            "Minetake.Basis.Luau.Tests.LuauScriptBenchmarkTests.LuauUpdate_SnapshotReadSingleProxy",
            "Minetake.Basis.Luau.Tests.LuauScriptBenchmarkTests.LuauUpdate_RotateCommandSingleProxy",
            "Minetake.Basis.Luau.Tests.LuauScriptBenchmarkTests.LuauUpdate_MassProps_SingleHost",
            "Minetake.Basis.Luau.Tests.LuauScriptBenchmarkTests.LuauUpdate_MassProps_ManyHosts",
            "Minetake.Basis.Luau.Tests.LuauScriptBenchmarkTests.LuauLoad_MemoryFootprintPerProxy",
        };

        static readonly string[] UgcBenchmarkTestNames =
        {
            "Minetake.Basis.Luau.Tests.LuauUgcScaleBenchmarkTests.SingleHost_DensityScaling",
            "Minetake.Basis.Luau.Tests.LuauUgcScaleBenchmarkTests.ManyHosts_UgcPopulationScaling",
            "Minetake.Basis.Luau.Tests.LuauUgcScaleBenchmarkTests.LoadBurst_AndAggregateMemory",
        };

        [MenuItem("Tools/Basis Luau/Benchmarks/Quick Regression/Run All")]
        public static void RunAll() => RunBenchmarks(TestMode.EditMode, thenPlayMode: true);

        [MenuItem("Tools/Basis Luau/Benchmarks/Quick Regression/Run Edit Mode")]
        public static void RunEditMode() => RunBenchmarks(TestMode.EditMode, thenPlayMode: false);

        [MenuItem("Tools/Basis Luau/Benchmarks/Quick Regression/Run Play Mode")]
        public static void RunPlayMode() => RunBenchmarks(TestMode.PlayMode, thenPlayMode: false);

        [MenuItem("Tools/Basis Luau/Benchmarks/Quick Regression/Run UGC Scale Play Mode")]
        public static void RunUgcScalePlayMode()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var filter = new Filter
            {
                testMode = TestMode.PlayMode,
                groupNames = new[] { "UgcBenchmark" },
            };
            api.RegisterCallbacks(new BenchmarkLogCallback(TestMode.PlayMode, null));
            api.Execute(new ExecutionSettings(filter));
        }

        public static void RunBenchmarks(TestMode mode, bool thenPlayMode)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var filter = new Filter
            {
                testMode = mode,
                testNames = mode == TestMode.PlayMode
                    ? Combine(CoreBenchmarkTestNames, UgcBenchmarkTestNames)
                    : CoreBenchmarkTestNames,
            };
            api.RegisterCallbacks(new BenchmarkLogCallback(mode, thenPlayMode ? TestMode.PlayMode : (TestMode?)null));
            api.Execute(new ExecutionSettings(filter));
            Debug.Log($"[BasisLuau.Benchmark] Started {mode} benchmarks. Watch console for [BasisLuau.Benchmark] output.");
        }

        static string[] Combine(string[] first, string[] second)
        {
            var combined = new string[first.Length + second.Length];
            first.CopyTo(combined, 0);
            second.CopyTo(combined, first.Length);
            return combined;
        }

        sealed class BenchmarkLogCallback : ICallbacks
        {
            readonly TestMode _mode;
            readonly TestMode? _nextMode;
            readonly StringBuilder _log = new();

            public BenchmarkLogCallback(TestMode mode, TestMode? nextMode)
            {
                _mode = mode;
                _nextMode = nextMode;
            }

            public void RunStarted(ITestAdaptor testsToRun) =>
                Debug.Log($"[BasisLuau.Benchmark] Running {_mode} benchmark suite ({testsToRun.TestCaseCount} tests)...");

            public void RunFinished(ITestResultAdaptor result)
            {
                _log.AppendLine($"[BasisLuau.Benchmark] {_mode} finished: pass={result.PassCount} fail={result.FailCount} skip={result.SkipCount}");
                CollectFailures(result);
                Debug.Log(_log.ToString());

                if (_nextMode.HasValue)
                {
                    EditorApplication.delayCall += () => RunBenchmarks(_nextMode.Value, thenPlayMode: false);
                }
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.Test.IsSuite || result.ResultState == "Passed")
                {
                    return;
                }

                _log.AppendLine($"  {result.Test.FullName}: {result.ResultState} {result.Message}");
            }

            static void CollectFailures(ITestResultAdaptor result)
            {
                if (!result.HasChildren)
                {
                    return;
                }

                foreach (ITestResultAdaptor child in result.Children)
                {
                    CollectFailures(child);
                }
            }
        }
    }
}
#endif
