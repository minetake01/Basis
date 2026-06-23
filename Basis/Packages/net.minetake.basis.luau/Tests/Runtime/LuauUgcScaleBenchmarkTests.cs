using System;
using System.Collections;
using System.Collections.Generic;
using Luau;
using Minetake.Basis.Luau.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Minetake.Basis.Luau.Tests
{
    [Category("Benchmark")]
    [Category("UgcBenchmark")]
    public sealed class LuauUgcScaleBenchmarkTests
    {
        const double LuauBudgetMs = 2.0;
        const double Strict90HzDeadlineMs = 1000.0 / 90.0;
        const int Samples = 240;
        const int WarmupSamples = 30;

        const string DormantSource = "return {}";

        const string LightSource = @"
local phase = 0
return {
    update = function(dt)
        phase = phase + dt
        if phase > 1 then
            phase = phase - 1
        end
    end,
}";

        const string ComputeSource = @"
return {
    update = function(dt)
        local sum = 0
        for i = 1, 500 do
            sum = sum + i * dt
        end
    end,
}";

        const string BridgeSource = @"
local HANDLE = 0
return {
    start = function()
        if __luau_handles and __luau_handles[1] then
            HANDLE = __luau_handles[1]
        end
    end,
    update = function(dt)
        local now = basis_time.now()
        if HANDLE ~= 0 and #now > 0 then
            transform.rotate(HANDLE, 0, 30 * dt, 0)
        end
    end,
}";

        byte[] _dormantBytecode;
        byte[] _lightBytecode;
        byte[] _computeBytecode;
        byte[] _bridgeBytecode;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            LuauBenchmarkHarness.RequireNativeRuntime();
            LuauBenchmarkHarness.ClearSession();
            LuauBenchmarkHarness.LogEnvironment();
            _dormantBytecode = LuauBenchmarkHarness.CompileSigned(DormantSource);
            _lightBytecode = LuauBenchmarkHarness.CompileSigned(LightSource);
            _computeBytecode = LuauBenchmarkHarness.CompileSigned(ComputeSource);
            _bridgeBytecode = LuauBenchmarkHarness.CompileSigned(BridgeSource);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() => LuauBenchmarkHarness.LogSessionSummary();

        [UnityTest]
        public IEnumerator SingleHost_DensityScaling()
        {
            foreach (int proxyCount in new[] { 32, 128, 256 })
            {
                UgcPopulation population = CreateSingleHostPopulation(proxyCount);
                yield return WaitUntilLoaded(population);

                MeasurePopulation(
                    $"single-host mixed UGC ({proxyCount} scripts)",
                    population,
                    $"topology=1 host x {proxyCount}; mix=50% dormant, 30% light, 15% compute, 5% bridge");

                population.Destroy();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ManyHosts_UgcPopulationScaling()
        {
            foreach (int hostCount in new[] { 32, 128, 512, 1000 })
            {
                UgcPopulation population = CreateManyHostPopulation(hostCount);
                yield return WaitUntilLoaded(population);

                MeasurePopulation(
                    $"many-host mixed UGC ({hostCount} scripts)",
                    population,
                    $"topology={hostCount} hosts x 1; mix=50% dormant, 30% light, 15% compute, 5% bridge");

                population.Destroy();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator LoadBurst_AndAggregateMemory()
        {
            const int scriptCount = 1000;
            LuauBenchmarkHarness.Sample baseline = LuauBenchmarkHarness.CaptureAggregateSample(Array.Empty<LuauState>());
            bool allocationCounterAvailable = LuauBenchmarkHarness.TryGetThreadAllocatedBytes(out long allocatedBefore);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            UgcPopulation population = CreateManyHostPopulation(scriptCount);
            stopwatch.Stop();
            long allocatedBytes = allocationCounterAvailable && LuauBenchmarkHarness.TryGetThreadAllocatedBytes(out long allocatedAfter)
                ? allocatedAfter - allocatedBefore
                : -1;

            yield return WaitUntilLoaded(population);
            LuauBenchmarkHarness.Sample loaded = LuauBenchmarkHarness.CaptureAggregateSample(population.RootStates);
            string allocationNote = allocatedBytes < 0
                ? "gcAlloc=unavailable"
                : $"gcAlloc={LuauBenchmarkHarness.FormatBytesPublic(allocatedBytes)} " +
                  $"({LuauBenchmarkHarness.FormatBytesPublic(allocatedBytes / scriptCount)}/script)";
            LuauBenchmarkHarness.RecordFrameBenchmark(
                "UGC Load",
                $"Load burst ({scriptCount} hosts/scripts)",
                scriptCount,
                stopwatch.Elapsed.TotalMilliseconds,
                baseline,
                loaded,
                $"{allocationNote}; aggregate Luau memory across all hosts");

            population.Destroy();
            yield return null;
        }

        void MeasurePopulation(string name, UgcPopulation population, string notes)
        {
            LuauBenchmarkHarness.MeasureDistribution(
                "UGC Scale",
                name,
                WarmupSamples,
                Samples,
                population.ScriptCount,
                () => population.PumpAll(1f / 90f),
                population.RootStates,
                LuauBudgetMs,
                Strict90HzDeadlineMs,
                notes);
        }

        UgcPopulation CreateSingleHostPopulation(int proxyCount)
        {
            var population = new UgcPopulation(proxyCount);
            GameObject hostObject = new GameObject($"ugc-density-{proxyCount}");
            LuauPropHost host = hostObject.AddComponent<LuauPropHost>();
            population.AddHost(hostObject, host);

            for (int i = 0; i < proxyCount; i++)
            {
                GameObject scriptObject = new GameObject($"ugc-script-{i}");
                scriptObject.transform.SetParent(hostObject.transform);
                AddMixedProxy(population, scriptObject, host, i);
            }

            return population;
        }

        UgcPopulation CreateManyHostPopulation(int hostCount)
        {
            var population = new UgcPopulation(hostCount);
            for (int i = 0; i < hostCount; i++)
            {
                GameObject hostObject = new GameObject($"ugc-host-{i}");
                LuauPropHost host = hostObject.AddComponent<LuauPropHost>();
                population.AddHost(hostObject, host);
                AddMixedProxy(population, hostObject, host, i);
            }

            return population;
        }

        void AddMixedProxy(UgcPopulation population, GameObject scriptObject, LuauPropHost host, int index)
        {
            int bucket = index % 20;
            byte[] bytecode;
            UnityEngine.Object[] slots;
            if (bucket < 10)
            {
                bytecode = _dormantBytecode;
                slots = Array.Empty<UnityEngine.Object>();
            }
            else if (bucket < 16)
            {
                bytecode = _lightBytecode;
                slots = Array.Empty<UnityEngine.Object>();
            }
            else if (bucket < 19)
            {
                bytecode = _computeBytecode;
                slots = Array.Empty<UnityEngine.Object>();
            }
            else
            {
                bytecode = _bridgeBytecode;
                slots = new UnityEngine.Object[] { scriptObject.transform };
            }

            LuauScriptProxy proxy = scriptObject.AddComponent<LuauScriptProxy>();
            proxy.Configure(LuauHostKind.Prop, bytecode, slots, $"ugc-{index}", host);
            population.AddProxy(proxy);
        }

        static IEnumerator WaitUntilLoaded(UgcPopulation population)
        {
            yield return null;
            yield return null;

            for (int i = 0; i < population.Proxies.Count; i++)
            {
                LuauScriptProxy proxy = population.Proxies[i];
                Assert.That(proxy.IsEnabled, Is.True, proxy.DisableReason);
                Assert.That(proxy.IsNativeRegistered, Is.True);
            }
        }

        sealed class UgcPopulation
        {
            readonly List<GameObject> _hostObjects;
            readonly List<LuauPropHost> _hosts;

            public readonly List<LuauScriptProxy> Proxies;
            public readonly List<LuauState> RootStates;

            public int ScriptCount => Proxies.Count;

            public UgcPopulation(int capacity)
            {
                _hostObjects = new List<GameObject>(capacity);
                _hosts = new List<LuauPropHost>(capacity);
                Proxies = new List<LuauScriptProxy>(capacity);
                RootStates = new List<LuauState>(capacity);
            }

            public void AddHost(GameObject hostObject, LuauPropHost host)
            {
                _hostObjects.Add(hostObject);
                _hosts.Add(host);
                RootStates.Add(host.RuntimeBridge.Native.CreateRootStateWrapper());
            }

            public void AddProxy(LuauScriptProxy proxy) => Proxies.Add(proxy);

            public void PumpAll(float deltaTime)
            {
                for (int i = 0; i < _hosts.Count; i++)
                {
                    _hosts[i].RuntimeBridge.PumpUpdate(deltaTime);
                }
            }

            public void Destroy()
            {
                for (int i = 0; i < _hostObjects.Count; i++)
                {
                    UnityEngine.Object.Destroy(_hostObjects[i]);
                }
            }
        }
    }
}
