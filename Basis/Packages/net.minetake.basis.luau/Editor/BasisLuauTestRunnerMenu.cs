#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Minetake.Basis.Luau.Editor
{
    public static class BasisLuauTestRunnerMenu
    {
        [MenuItem("Tools/Basis Luau/Run Luau Tests (Edit Mode)")]
        public static void RunEditModeTests() => RunTests(TestMode.EditMode);

        [MenuItem("Tools/Basis Luau/Run Luau Tests (Play Mode)")]
        public static void RunPlayModeTests() => RunTests(TestMode.PlayMode);

        static void RunTests(TestMode mode)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var filter = new Filter
            {
                testMode = mode,
                assemblyNames = new[] { "Minetake.Basis.Luau.Tests" },
            };
            api.RegisterCallbacks(new LogCallback(mode));
            api.Execute(new ExecutionSettings(filter));
        }

        sealed class LogCallback : ICallbacks
        {
            readonly TestMode _mode;
            readonly StringBuilder _log = new();

            public LogCallback(TestMode mode) => _mode = mode;

            public void RunStarted(ITestAdaptor testsToRun) =>
                Debug.Log($"[BasisLuau] Starting {_mode} tests...");

            public void RunFinished(ITestResultAdaptor result)
            {
                _log.AppendLine($"[BasisLuau] {_mode} finished: pass={result.PassCount} fail={result.FailCount} skip={result.SkipCount}");
                CollectFailures(result);
                Debug.Log(_log.ToString());
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
