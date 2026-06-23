using System.Collections;
using System.Collections.Generic;
using Luau;
using Luau.Unity;
using Minetake.Basis.Luau.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Minetake.Basis.Luau.Tests
{
    [Category("Benchmark")]
    public sealed class LuauScriptBenchmarkTests
    {
        const string MinimalUpdateSource = @"
return {
    update = function(dt)
    end,
}";

        const string ComputeUpdateSource = @"
return {
    update = function(dt)
        local sum = 0
        for i = 1, 500 do
            sum = sum + i * dt
        end
    end,
}";

        const string SnapshotReadSource = @"
return {
    update = function(dt)
        local t = basis_time.deltaTime()
        local now = basis_time.now()
        local _ = t + #now
    end,
}";

        const string RotateCommandSource = @"
local HANDLE = 0
return {
    start = function()
        if __luau_handles and __luau_handles[1] then
            HANDLE = __luau_handles[1]
        end
    end,
    update = function(dt)
        if HANDLE ~= 0 then
            transform.rotate(HANDLE, 0, 45 * dt, 0)
        end
    end,
}";

        [OneTimeSetUp]
        public void OneTimeSetUp() => LuauBenchmarkHarness.ClearSession();

        [OneTimeTearDown]
        public void OneTimeTearDown() => LuauBenchmarkHarness.LogSessionSummary();

        [UnityTest]
        public IEnumerator LuauUpdate_MinimalSingleProxy()
        {
            yield return RunFrameBenchmark(
                "Luau",
                "Native update (empty body, 1 proxy)",
                MinimalUpdateSource,
                proxyCount: 1,
                frames: 600,
                registerTransform: false);
        }

        [UnityTest]
        public IEnumerator LuauUpdate_ComputeSingleProxy()
        {
            yield return RunFrameBenchmark(
                "Luau",
                "Native update (500-iter loop, 1 proxy)",
                ComputeUpdateSource,
                proxyCount: 1,
                frames: 300,
                registerTransform: false);
        }

        [UnityTest]
        public IEnumerator LuauUpdate_SnapshotReadSingleProxy()
        {
            yield return RunFrameBenchmark(
                "Luau",
                "Native update (basis_time reads, 1 proxy)",
                SnapshotReadSource,
                proxyCount: 1,
                frames: 300,
                registerTransform: false);
        }

        [UnityTest]
        public IEnumerator LuauUpdate_RotateCommandSingleProxy()
        {
            yield return RunFrameBenchmark(
                "Luau",
                "Native update (transform.rotate, 1 proxy)",
                RotateCommandSource,
                proxyCount: 1,
                frames: 300,
                registerTransform: true);
        }

        [UnityTest]
        public IEnumerator LuauUpdate_MassProps_SingleHost()
        {
            foreach (int count in new[] { 32, 128 })
            {
                yield return RunFrameBenchmark(
                    "Luau",
                    $"Mass props single-host ({count} proxies, compute update)",
                    ComputeUpdateSource,
                    proxyCount: count,
                    frames: 60,
                    registerTransform: false);
            }
        }

        [UnityTest]
        public IEnumerator LuauUpdate_MassProps_ManyHosts()
        {
            LuauBenchmarkHarness.RequireNativeRuntime();
            const int hostCount = 32;
            var hosts = new List<GameObject>(hostCount);
            var proxies = new List<LuauScriptProxy>(hostCount);
            byte[] bytecode = LuauBenchmarkHarness.CompileSigned(ComputeUpdateSource);

            try
            {
                for (int i = 0; i < hostCount; i++)
                {
                    GameObject hostGo = new GameObject($"mass-host-{i}");
                    hosts.Add(hostGo);
                    LuauPropHost host = hostGo.AddComponent<LuauPropHost>();
                    LuauScriptProxy proxy = hostGo.AddComponent<LuauScriptProxy>();
                    proxy.Configure(
                        LuauHostKind.Prop,
                        bytecode,
                        System.Array.Empty<Object>(),
                        $"mass-host-{i}",
                        host);
                    proxies.Add(proxy);
                }

                yield return null;
                yield return null;

                for (int i = 0; i < proxies.Count; i++)
                {
                    Assert.That(proxies[i].IsEnabled, Is.True, proxies[i].DisableReason);
                    Assert.That(proxies[i].IsNativeRegistered, Is.True);
                }

                LuauState root = hosts[0].GetComponent<LuauPropHost>().RuntimeBridge.Native.CreateRootStateWrapper();
                LuauBenchmarkHarness.Sample before = LuauBenchmarkHarness.CaptureSample(root);

                const int frames = 60;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int frame = 0; frame < frames; frame++)
                {
                    yield return null;
                }

                sw.Stop();
                LuauBenchmarkHarness.Sample after = LuauBenchmarkHarness.CaptureSample(root);

                LuauBenchmarkHarness.RecordFrameBenchmark(
                    "Luau",
                    $"Mass props many-hosts ({hostCount} hosts)",
                    frames,
                    sw.Elapsed.TotalMilliseconds,
                    before,
                    after,
                    $"{hostCount} independent VM hosts");
            }
            finally
            {
                for (int i = 0; i < hosts.Count; i++)
                {
                    Object.Destroy(hosts[i]);
                }
            }
        }

        [UnityTest]
        public IEnumerator LuauLoad_MemoryFootprintPerProxy()
        {
            LuauBenchmarkHarness.RequireNativeRuntime();
            var hostGo = new GameObject("bench-load-memory");
            var host = hostGo.AddComponent<LuauPropHost>();
            byte[] bytecode = LuauBenchmarkHarness.CompileSigned(ComputeUpdateSource);

            yield return null;

            LuauState root = host.RuntimeBridge.Native.CreateRootStateWrapper();
            LuauBenchmarkHarness.Sample baseline = LuauBenchmarkHarness.CaptureSample(root);

            const int loadCount = 32;
            var proxyObjects = new GameObject[loadCount];
            for (int i = 0; i < loadCount; i++)
            {
                proxyObjects[i] = new GameObject($"load-proxy-{i}");
                proxyObjects[i].transform.SetParent(hostGo.transform);
                LuauScriptProxy proxy = proxyObjects[i].AddComponent<LuauScriptProxy>();
                proxy.Configure(LuauHostKind.Prop, bytecode, System.Array.Empty<Object>(), $"load-{i}", host);
            }

            yield return null;

            LuauBenchmarkHarness.Sample loaded = LuauBenchmarkHarness.CaptureSample(root);
            long managedDelta = loaded.ManagedBytes - baseline.ManagedBytes;
            long luauDelta = (long)(loaded.LuauBytes - baseline.LuauBytes);

            UnityEngine.Debug.Log(
                $"[BasisLuau.Benchmark] [Luau] Load memory ({loadCount} proxies on 1 host): " +
                $"managedΔ={FormatDelta(managedDelta)} ({FormatBytes(managedDelta / loadCount)}/proxy), " +
                $"luauΔ={FormatDelta(luauDelta)} ({FormatBytes(luauDelta / loadCount)}/proxy), " +
                $"total luau={FormatBytes((long)loaded.LuauBytes)}/{FormatBytes((long)loaded.LuauCapBytes)}");

            for (int i = 0; i < loadCount; i++)
            {
                Assert.That(proxyObjects[i].GetComponent<LuauScriptProxy>().IsEnabled, Is.True);
            }

            Object.Destroy(hostGo);
        }

        static IEnumerator RunFrameBenchmark(
            string layer,
            string name,
            string source,
            int proxyCount,
            int frames,
            bool registerTransform)
        {
            LuauBenchmarkHarness.RequireNativeRuntime();
            var hostGo = new GameObject("bench-luau-frames");
            var host = hostGo.AddComponent<LuauPropHost>();
            byte[] bytecode = LuauBenchmarkHarness.CompileSigned(source);
            Object[] slots = System.Array.Empty<Object>();
            if (registerTransform)
            {
                slots = new Object[] { hostGo.transform };
            }

            var proxies = new LuauScriptProxy[proxyCount];
            for (int i = 0; i < proxyCount; i++)
            {
                GameObject child = proxyCount == 1
                    ? hostGo
                    : new GameObject($"proxy-{i}");
                if (proxyCount > 1)
                {
                    child.transform.SetParent(hostGo.transform);
                }

                LuauScriptProxy proxy = child.GetComponent<LuauScriptProxy>() ?? child.AddComponent<LuauScriptProxy>();
                proxy.Configure(LuauHostKind.Prop, bytecode, slots, $"bench-{i}", host);
                proxies[i] = proxy;
            }

            yield return null;
            yield return null;

            for (int i = 0; i < proxies.Length; i++)
            {
                Assert.That(proxies[i].IsEnabled, Is.True, proxies[i].DisableReason);
                Assert.That(proxies[i].IsNativeRegistered, Is.True);
            }

            LuauState root = host.RuntimeBridge.Native.CreateRootStateWrapper();
            LuauBenchmarkHarness.Sample before = LuauBenchmarkHarness.CaptureSample(root);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int frame = 0; frame < frames; frame++)
            {
                yield return null;
            }

            sw.Stop();
            LuauBenchmarkHarness.Sample after = LuauBenchmarkHarness.CaptureSample(root);

            LuauBenchmarkHarness.RecordFrameBenchmark(
                layer,
                name,
                frames,
                sw.Elapsed.TotalMilliseconds,
                before,
                after,
                $"{proxyCount} proxies");

            Object.Destroy(hostGo);
        }

        static string FormatBytes(long bytes) => LuauBenchmarkHarness.FormatBytesPublic(bytes);

        static string FormatDelta(long bytes) => bytes >= 0 ? $"+{FormatBytes(bytes)}" : $"-{FormatBytes(-bytes)}";
    }
}
