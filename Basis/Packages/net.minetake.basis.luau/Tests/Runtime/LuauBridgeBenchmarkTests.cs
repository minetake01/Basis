using System.Collections;
using Luau;
using Luau.Unity;
using Minetake.Basis.Luau.Registry;
using Minetake.Basis.Luau.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Minetake.Basis.Luau.Tests
{
    [Category("Benchmark")]
    public sealed class LuauBridgeBenchmarkTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp() => LuauBenchmarkHarness.ClearSession();

        [OneTimeTearDown]
        public void OneTimeTearDown() => LuauBenchmarkHarness.LogSessionSummary();

        [UnityTest]
        public IEnumerator CommandRing_PushPopThroughput()
        {
            LuauBenchmarkHarness.RequireNativeRuntime();
            var hostGo = new GameObject("bench-bridge-ring");
            var host = hostGo.AddComponent<LuauPropHost>();

            yield return null;

            var bridge = host.RuntimeBridge;
            BasisLuauNativeRuntime native = bridge.Native;
            Assert.That(native.IsCreated, Is.True);

            var cmd = new BasisLuauCommandNative
            {
                Type = (ushort)LuauCommandType.SetPosition,
                HostId = (ushort)bridge.Authority.HostId,
            };

            LuauBenchmarkHarness.Measure(
                "Bridge",
                "CommandRing.Push+Pop",
                warmup: 500,
                iterations: 50_000,
                () =>
                {
                    native.PushCommand(bridge.Authority.HostId, ref cmd);
                    native.PopCommand(out _);
                },
                rootState: host.HasLiveState ? host.RuntimeBridge.Native.CreateRootStateWrapper() : null);

            Object.Destroy(hostGo);
        }

        [UnityTest]
        public IEnumerator SnapshotPublish_WithTransformSlots()
        {
            LuauBenchmarkHarness.RequireNativeRuntime();
            const int transformCount = 64;
            var hostGo = new GameObject("bench-bridge-snapshot");
            var host = hostGo.AddComponent<LuauPropHost>();

            yield return null;

            for (int i = 0; i < transformCount; i++)
            {
                var child = new GameObject($"snap{i}").transform;
                child.SetParent(hostGo.transform);
                host.RegisterObject(child);
            }

            LuauHostRuntimeBridge bridge = host.RuntimeBridge;
            LuauState root = host.RuntimeBridge.Native.CreateRootStateWrapper();

            LuauBenchmarkHarness.Measure(
                "Bridge",
                $"SnapshotPublish({transformCount} transforms)",
                warmup: 20,
                iterations: 500,
                () => bridge.PumpUpdate(0.016f),
                rootState: root,
                notes: $"{transformCount} transform slots + time slot");

            Object.Destroy(hostGo);
        }

        [UnityTest]
        public IEnumerator FullPumpUpdate_NoProxies()
        {
            LuauBenchmarkHarness.RequireNativeRuntime();
            var hostGo = new GameObject("bench-bridge-pump");
            var host = hostGo.AddComponent<LuauPropHost>();

            yield return null;

            LuauHostRuntimeBridge bridge = host.RuntimeBridge;
            LuauState root = bridge.Native.CreateRootStateWrapper();

            LuauBenchmarkHarness.Measure(
                "Bridge",
                "PumpUpdate (flush+tickets+snapshot+scheduler)",
                warmup: 30,
                iterations: 1_000,
                () => bridge.PumpUpdate(0.016f),
                rootState: root);

            Object.Destroy(hostGo);
        }

        [UnityTest]
        public IEnumerator FullPumpUpdate_WithCommandBacklog()
        {
            LuauBenchmarkHarness.RequireNativeRuntime();
            var hostGo = new GameObject("bench-bridge-backlog");
            var host = hostGo.AddComponent<LuauPropHost>();

            yield return null;

            LuauObjectHandle handle = host.RegisterObject(hostGo.transform);
            LuauHostRuntimeBridge bridge = host.RuntimeBridge;
            BasisLuauNativeRuntime native = bridge.Native;
            LuauState root = native.CreateRootStateWrapper();

            const int commandsPerFrame = 32;
            LuauBenchmarkHarness.Measure(
                "Bridge",
                $"PumpUpdate with {commandsPerFrame} deferred commands",
                warmup: 10,
                iterations: 500,
                () =>
                {
                    for (int i = 0; i < commandsPerFrame; i++)
                    {
                        var cmd = new BasisLuauCommandNative
                        {
                            Type = (ushort)LuauCommandType.SetPosition,
                            HostId = (ushort)bridge.Authority.HostId,
                            HandleIndex = handle.Index,
                            HandleGeneration = handle.Generation,
                        };
                        PushPositionX(ref cmd, i);

                        native.PushCommand(bridge.Authority.HostId, ref cmd);
                    }

                    bridge.PumpUpdate(0.016f);
                },
                rootState: root);

            Object.Destroy(hostGo);
        }

        static unsafe void PushPositionX(ref BasisLuauCommandNative cmd, float x) => cmd.Data[0] = x;
    }
}
