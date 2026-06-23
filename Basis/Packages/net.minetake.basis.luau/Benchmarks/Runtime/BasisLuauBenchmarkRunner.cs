using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Luau;
using Luau.Unity;
using Minetake.Basis.Luau.Registry;
using Minetake.Basis.Luau.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Scripting;

namespace Minetake.Basis.Luau.Benchmarks
{
    public enum BasisLuauBenchmarkProfile
    {
        Smoke,
        Full,
    }

    [DisallowMultipleComponent]
    public sealed class BasisLuauBenchmarkRunner : MonoBehaviour
    {
        const double LuauBudgetMs = 2.0;
        const double Strict90HzDeadlineMs = 1000.0 / 90.0;
        const string OutputFolderName = "BasisLuauBenchmarks";

        [SerializeField] BasisLuauBenchmarkProfile profile = BasisLuauBenchmarkProfile.Smoke;
        [SerializeField] bool requireIl2CppPlayer;
        [SerializeField] bool quitOnComplete;
        [SerializeField] string buildGitCommit = "unknown";
        [SerializeField] string nativeDllHash = "unknown";

        readonly List<GameObject> _ownedObjects = new();

        public void Configure(
            BasisLuauBenchmarkProfile benchmarkProfile,
            bool requireIl2Cpp,
            bool quitWhenComplete,
            string gitCommit,
            string runtimeHash)
        {
            profile = benchmarkProfile;
            requireIl2CppPlayer = requireIl2Cpp;
            quitOnComplete = quitWhenComplete;
            buildGitCommit = string.IsNullOrWhiteSpace(gitCommit) ? "unknown" : gitCommit;
            nativeDllHash = string.IsNullOrWhiteSpace(runtimeHash) ? "unknown" : runtimeHash;
        }

        IEnumerator Start()
        {
            yield return null;

            BasisLuauBenchmarkReport report = null;
            string failure = null;
            try
            {
                report = RunBenchmarks();
            }
            catch (Exception ex)
            {
                failure = ex.ToString();
                UnityEngine.Debug.LogError($"[BasisLuau.Benchmark] failed: {failure}");
            }
            finally
            {
                DestroyOwnedObjects();
            }

            if (report != null)
            {
                WriteArtifacts(report);
            }

            if (failure != null)
            {
                WriteFailureArtifact(failure);
            }

            if (quitOnComplete)
            {
                Application.Quit(failure == null ? 0 : 1);
            }
        }

        BasisLuauBenchmarkReport RunBenchmarks()
        {
            ValidateRuntime();

            var report = new BasisLuauBenchmarkReport
            {
                schemaVersion = 2,
                generatedUtc = DateTime.UtcNow.ToString("O"),
                profile = profile.ToString(),
                environment = CaptureEnvironment(),
                acceptance = BasisLuauBenchmarkAcceptance.Default(),
            };

            BasisLuauBenchmarkSettings settings = BasisLuauBenchmarkSettings.For(profile);
            report.workloadHash = settings.WorkloadHash;

            using (BenchmarkNativeHost nativeHost = CreateNativeHost())
            {
                RunNativeBenchmarks(report, settings, nativeHost);
                RunBridgeBenchmarks(report, settings);
                RunScenarioBenchmarks(report, settings);
            }

            report.acceptance.Evaluate(report.results, TryLoadBaselinePrimary(report.acceptance.primaryCaseId));
            return report;
        }

        void ValidateRuntime()
        {
            if (requireIl2CppPlayer)
            {
                if (Application.isEditor)
                {
                    throw new InvalidOperationException("IL2CPP Player benchmark must not run inside the Editor.");
                }

#if !ENABLE_IL2CPP
                throw new InvalidOperationException("IL2CPP Player benchmark requires ENABLE_IL2CPP.");
#else
                if (UnityEngine.Debug.isDebugBuild)
                {
                    throw new InvalidOperationException("IL2CPP Player benchmark must be a non-development build.");
                }

                if (Application.platform != RuntimePlatform.WindowsPlayer)
                {
                    throw new InvalidOperationException($"Windows x64 Player benchmark required, got {Application.platform}.");
                }
#endif
            }

            if (!BasisLuauNativeRuntime.IsAvailable)
            {
                throw new InvalidOperationException(
                    $"Basis Luau native runtime is required: {BasisLuauNativeRuntime.UnavailableReason}");
            }
        }

        BasisLuauBenchmarkEnvironment CaptureEnvironment() => new()
        {
            unityVersion = Application.unityVersion,
            platform = Application.platform.ToString(),
            scriptingBackend = GetScriptingBackendName(),
            il2cpp = IsIl2Cpp(),
            developmentBuild = UnityEngine.Debug.isDebugBuild,
            cpu = SystemInfo.processorType,
            coreCount = SystemInfo.processorCount,
            systemMemory = SystemInfo.systemMemorySize,
            graphicsDevice = SystemInfo.graphicsDeviceName,
            incrementalGc = GarbageCollector.isIncremental,
            packageVersion = BasisLuauBenchmarkPackageInfo.Version,
            gitCommit = buildGitCommit,
            nativeDllHash = nativeDllHash,
        };

        static string GetScriptingBackendName()
        {
#if ENABLE_IL2CPP
            return "IL2CPP";
#elif ENABLE_MONO
            return "Mono";
#else
            return "Unknown";
#endif
        }

        static bool IsIl2Cpp()
        {
#if ENABLE_IL2CPP
            return true;
#else
            return false;
#endif
        }

        static BenchmarkNativeHost CreateNativeHost()
        {
            var native = new BasisLuauNativeRuntime();
            var limits = new LuauExecutionLimits { MemoryBudgetBytes = 256 * 1024 * 1024 };
            native.Create(limits, 1, (int)LuauHostKind.Prop);
            native.StartScheduler(0);
            return new BenchmarkNativeHost(native, limits.RootState);
        }

        void RunNativeBenchmarks(
            BasisLuauBenchmarkReport report,
            BasisLuauBenchmarkSettings settings,
            BenchmarkNativeHost nativeHost)
        {
            BasisLuauCommandNative cmd = CreateCommand(LuauCommandType.SetPosition, 1);
            AddResult(
                report,
                Measure(
                    "native.command-ring-small",
                    "Native",
                    "small",
                    settings.NativeWarmup,
                    settings.NativeIterations,
                    1,
                    nativeHost.RootStates,
                    () =>
                    {
                        Require(nativeHost.Native.PushCommand(1, ref cmd) == BasisLuauRingResult.Ok, "command ring push failed");
                        Require(nativeHost.Native.PopCommand(out _) == BasisLuauRingResult.Ok, "command ring pop failed");
                    }));

            AddResult(
                report,
                Measure(
                    "native.snapshot-medium-128",
                    "Native",
                    "medium",
                    settings.MicroWarmup,
                    settings.MicroIterations,
                    128,
                    nativeHost.RootStates,
                    () => PublishSnapshotBatch(nativeHost.Native, 128)));

            foreach (int payloadBytes in settings.LargePayloadBytes)
            {
                AddResult(
                    report,
                    Measure(
                        $"native.buffer-large-{payloadBytes}",
                        "Native",
                        "large",
                        settings.MicroWarmup,
                        settings.MicroIterations,
                        payloadBytes,
                        nativeHost.RootStates,
                        () => AllocateReadReleaseBuffer(nativeHost.Native, payloadBytes)));
            }

            AddResult(
                report,
                Measure(
                    "native.scheduler-tick",
                    "Native",
                    "small",
                    settings.MicroWarmup,
                    settings.MicroIterations,
                    1,
                    nativeHost.RootStates,
                    () => nativeHost.Native.KickScheduler(1f / 90f, Time.fixedDeltaTime, false)));
        }

        void RunBridgeBenchmarks(BasisLuauBenchmarkReport report, BasisLuauBenchmarkSettings settings)
        {
            using (BenchmarkPopulation population = CreateBridgePopulation(settings, settings.BridgeProxyCount))
            {
                AddResult(
                    report,
                    Measure(
                        $"bridge.mixed-{settings.BridgeProxyCount}",
                        "Bridge",
                        "mixed",
                        settings.FrameWarmup,
                        settings.FrameSamples,
                        settings.BridgeProxyCount,
                        population.RootStates,
                        () => population.PumpAll(1f / 90f)));
            }

            foreach (int payloadBytes in settings.LargePayloadBytes)
            {
                using BenchmarkPopulation population = CreateLargePayloadBridgePopulation(payloadBytes);
                AddResult(
                    report,
                    Measure(
                        $"bridge.large-payload-{payloadBytes}",
                        "Bridge",
                        "large",
                        settings.FrameWarmup,
                        settings.FrameSamples,
                        payloadBytes,
                        population.RootStates,
                        () => population.PumpAll(1f / 90f)));
            }
        }

        void RunScenarioBenchmarks(BasisLuauBenchmarkReport report, BasisLuauBenchmarkSettings settings)
        {
            for (int i = 0; i < settings.ScaleHosts.Length; i++)
            {
                int hostCount = settings.ScaleHosts[i];
                using BenchmarkPopulation population = CreateScenarioPopulation(hostCount, heavyPayload: false);
                BasisLuauBenchmarkResult result = Measure(
                    $"scenario.mixed-ugc-{hostCount}",
                    "Scenario",
                    "mixed",
                    settings.FrameWarmup,
                    settings.FrameSamples,
                    hostCount,
                    population.RootStates,
                    () => population.PumpAll(1f / 90f),
                    hostCount: hostCount,
                    proxyCount: hostCount,
                    topology: "multi-host",
                    cohort: "mixed",
                    tags: new[] { "scenario", "mixed", "multi-host" });
                AddResult(report, result);
                AddResult(report, CloneResult(result, $"topology.multi-host-mixed-{hostCount}", "Topology", "multi-host"));
            }

            using BenchmarkPopulation heavy = CreateScenarioPopulation(settings.HeavyScenarioHosts, heavyPayload: true);
            AddResult(
                report,
                Measure(
                    $"scenario.heavy-payload-mix-{settings.HeavyScenarioHosts}",
                    "Scenario",
                    "mixed",
                    settings.FrameWarmup,
                        settings.FrameSamples,
                        settings.HeavyScenarioHosts,
                        heavy.RootStates,
                        () => heavy.PumpAll(1f / 90f),
                        hostCount: settings.HeavyScenarioHosts,
                        proxyCount: settings.HeavyScenarioHosts,
                        topology: "multi-host",
                        cohort: "heavy-payload-mix",
                        tags: new[] { "scenario", "heavy-payload", "multi-host" }));

            RunScenarioCohortBenchmarks(report, settings);
            RunTopologyBenchmarks(report, settings);
            RunPhaseDiagnosticBenchmarks(report, settings);
        }

        void RunScenarioCohortBenchmarks(BasisLuauBenchmarkReport report, BasisLuauBenchmarkSettings settings)
        {
            string[] cohorts = { "dormant", "light", "compute", "bridge" };
            for (int cohortIndex = 0; cohortIndex < cohorts.Length; cohortIndex++)
            {
                string cohort = cohorts[cohortIndex];
                for (int i = 0; i < settings.ScaleHosts.Length; i++)
                {
                    int hostCount = settings.ScaleHosts[i];
                    using BenchmarkPopulation population = CreateScenarioCohortPopulation(hostCount, cohort);
                    AddResult(
                        report,
                        Measure(
                            $"scenario.cohort-{cohort}-{hostCount}",
                            "Scenario",
                            cohort,
                            settings.FrameWarmup,
                            settings.FrameSamples,
                            hostCount,
                            population.RootStates,
                            () => population.PumpAll(1f / 90f),
                            hostCount: hostCount,
                            proxyCount: hostCount,
                            topology: "multi-host",
                            cohort: cohort,
                            tags: new[] { "scenario", "cohort", cohort, "multi-host" }));
                }
            }

            int[] largePayloadHosts = profile == BasisLuauBenchmarkProfile.Full
                ? new[] { 128, 512, 1000 }
                : new[] { 32 };
            for (int i = 0; i < largePayloadHosts.Length; i++)
            {
                int hostCount = largePayloadHosts[i];
                using BenchmarkPopulation population = CreateScenarioCohortPopulation(hostCount, "large-payload");
                AddResult(
                    report,
                    Measure(
                        $"scenario.cohort-large-payload-{hostCount}",
                        "Scenario",
                        "large",
                        settings.FrameWarmup,
                        settings.FrameSamples,
                        hostCount,
                        population.RootStates,
                        () => population.PumpAll(1f / 90f),
                        hostCount: hostCount,
                        proxyCount: hostCount,
                        topology: "multi-host",
                        cohort: "large-payload",
                        tags: new[] { "scenario", "cohort", "large-payload", "multi-host" }));
            }
        }

        void RunTopologyBenchmarks(BasisLuauBenchmarkReport report, BasisLuauBenchmarkSettings settings)
        {
            for (int i = 0; i < settings.SingleHostProxyCounts.Length; i++)
            {
                int proxyCount = settings.SingleHostProxyCounts[i];
                using BenchmarkPopulation population = CreateSingleHostMixedPopulation(proxyCount);
                AddResult(
                    report,
                    Measure(
                        $"topology.single-host-mixed-{proxyCount}",
                        "Topology",
                        "mixed",
                        settings.FrameWarmup,
                        settings.FrameSamples,
                        proxyCount,
                        population.RootStates,
                        () => population.PumpAll(1f / 90f),
                        hostCount: 1,
                        proxyCount: proxyCount,
                        topology: "single-host",
                        cohort: "mixed",
                        tags: new[] { "topology", "single-host", "mixed" }));
            }
        }

        void RunPhaseDiagnosticBenchmarks(BasisLuauBenchmarkReport report, BasisLuauBenchmarkSettings settings)
        {
            int hostCount = profile == BasisLuauBenchmarkProfile.Full ? 1000 : 32;
            using BenchmarkPopulation population = CreateScenarioPopulation(hostCount, heavyPayload: false);
            AddResult(
                report,
                MeasureWithPhaseBreakdown(
                    $"diagnostic.phase-mixed-ugc-{hostCount}",
                    "Diagnostic",
                    "mixed",
                    settings.FrameWarmup,
                    settings.FrameSamples,
                    hostCount,
                    population.RootStates,
                    population,
                    hostCount: hostCount,
                    proxyCount: hostCount,
                    topology: "multi-host",
                    cohort: "mixed",
                    tags: new[] { "diagnostic", "phase", "mixed", "multi-host" }));
        }

        BenchmarkPopulation CreateBridgePopulation(BasisLuauBenchmarkSettings settings, int proxyCount)
        {
            var population = new BenchmarkPopulation();
            GameObject hostObject = CreateOwnedObject($"bench-bridge-host-{proxyCount}");
            LuauPropHost host = hostObject.AddComponent<LuauPropHost>();
            population.AddHost(hostObject, host);

            for (int i = 0; i < proxyCount; i++)
            {
                GameObject scriptObject = CreateOwnedObject($"bench-bridge-proxy-{i}");
                scriptObject.transform.SetParent(hostObject.transform);
                AddMixedProxy(population, scriptObject, host, i, heavyPayload: false);
            }

            WaitUntilLoaded(population);
            return population;
        }

        BenchmarkPopulation CreateLargePayloadBridgePopulation(int payloadBytes)
        {
            var population = new BenchmarkPopulation();
            GameObject hostObject = CreateOwnedObject($"bench-bridge-large-{payloadBytes}");
            LuauPropHost host = hostObject.AddComponent<LuauPropHost>();
            population.AddHost(hostObject, host);

            GameObject scriptObject = CreateOwnedObject($"bench-large-payload-{payloadBytes}");
            scriptObject.transform.SetParent(hostObject.transform);
            TMP_Text text = scriptObject.AddComponent<TextMeshPro>();
            AddProxy(
                population,
                scriptObject,
                host,
                LargePayloadSource(payloadBytes),
                new UnityEngine.Object[] { text },
                $"bridge-large-{payloadBytes}");

            WaitUntilLoaded(population);
            return population;
        }

        BenchmarkPopulation CreateScenarioPopulation(int hostCount, bool heavyPayload)
        {
            var population = new BenchmarkPopulation();
            for (int i = 0; i < hostCount; i++)
            {
                GameObject hostObject = CreateOwnedObject($"bench-scenario-host-{i}");
                LuauPropHost host = hostObject.AddComponent<LuauPropHost>();
                population.AddHost(hostObject, host);
                AddMixedProxy(population, hostObject, host, i, heavyPayload);
            }

            WaitUntilLoaded(population);
            return population;
        }

        BenchmarkPopulation CreateScenarioCohortPopulation(int hostCount, string cohort)
        {
            var population = new BenchmarkPopulation();
            for (int i = 0; i < hostCount; i++)
            {
                GameObject hostObject = CreateOwnedObject($"bench-cohort-{cohort}-host-{i}");
                LuauPropHost host = hostObject.AddComponent<LuauPropHost>();
                population.AddHost(hostObject, host);
                AddCohortProxy(population, hostObject, host, cohort, i);
            }

            WaitUntilLoaded(population);
            return population;
        }

        BenchmarkPopulation CreateSingleHostMixedPopulation(int proxyCount)
        {
            var population = new BenchmarkPopulation();
            GameObject hostObject = CreateOwnedObject($"bench-topology-single-host-{proxyCount}");
            LuauPropHost host = hostObject.AddComponent<LuauPropHost>();
            population.AddHost(hostObject, host);

            for (int i = 0; i < proxyCount; i++)
            {
                GameObject scriptObject = CreateOwnedObject($"bench-topology-single-proxy-{i}");
                scriptObject.transform.SetParent(hostObject.transform);
                AddMixedProxy(population, scriptObject, host, i, heavyPayload: false);
            }

            WaitUntilLoaded(population);
            return population;
        }

        void AddMixedProxy(
            BenchmarkPopulation population,
            GameObject scriptObject,
            LuauPropHost host,
            int index,
            bool heavyPayload)
        {
            int bucket = index % 20;
            if (heavyPayload && bucket == 19)
            {
                TMP_Text text = scriptObject.AddComponent<TextMeshPro>();
                AddProxy(
                    population,
                    scriptObject,
                    host,
                    LargePayloadSource(4096),
                    new UnityEngine.Object[] { text },
                    $"heavy-{index}");
                return;
            }

            if (bucket < 10)
            {
                AddProxy(population, scriptObject, host, DormantSource, Array.Empty<UnityEngine.Object>(), $"dormant-{index}");
            }
            else if (bucket < 16)
            {
                AddProxy(population, scriptObject, host, LightSource, Array.Empty<UnityEngine.Object>(), $"light-{index}");
            }
            else if (bucket < 19)
            {
                AddProxy(population, scriptObject, host, ComputeSource, Array.Empty<UnityEngine.Object>(), $"compute-{index}");
            }
            else
            {
                AddProxy(population, scriptObject, host, BridgeSource, new UnityEngine.Object[] { scriptObject.transform }, $"bridge-{index}");
            }
        }

        void AddCohortProxy(
            BenchmarkPopulation population,
            GameObject scriptObject,
            LuauPropHost host,
            string cohort,
            int index)
        {
            switch (cohort)
            {
                case "dormant":
                    AddProxy(population, scriptObject, host, DormantSource, Array.Empty<UnityEngine.Object>(), $"cohort-dormant-{index}");
                    break;
                case "light":
                    AddProxy(population, scriptObject, host, LightSource, Array.Empty<UnityEngine.Object>(), $"cohort-light-{index}");
                    break;
                case "compute":
                    AddProxy(population, scriptObject, host, ComputeSource, Array.Empty<UnityEngine.Object>(), $"cohort-compute-{index}");
                    break;
                case "bridge":
                    AddProxy(population, scriptObject, host, BridgeSource, new UnityEngine.Object[] { scriptObject.transform }, $"cohort-bridge-{index}");
                    break;
                case "large-payload":
                    TMP_Text text = scriptObject.AddComponent<TextMeshPro>();
                    AddProxy(population, scriptObject, host, LargePayloadSource(4096), new UnityEngine.Object[] { text }, $"cohort-large-{index}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(cohort), cohort, "Unknown benchmark cohort.");
            }
        }

        static void AddProxy(
            BenchmarkPopulation population,
            GameObject scriptObject,
            LuauPropHost host,
            string source,
            UnityEngine.Object[] slots,
            string moduleName)
        {
            byte[] bytecode = LuauBytecodeSigner.AttachSignature(LuauCompiler.Compile(Encoding.UTF8.GetBytes(source)));
            LuauScriptProxy proxy = scriptObject.AddComponent<LuauScriptProxy>();
            proxy.Configure(LuauHostKind.Prop, bytecode, slots, moduleName, host);
            population.AddProxy(proxy);
        }

        static void WaitUntilLoaded(BenchmarkPopulation population)
        {
            for (int i = 0; i < population.Proxies.Count; i++)
            {
                LuauScriptProxy proxy = population.Proxies[i];
                if (!proxy.IsEnabled || !proxy.IsNativeRegistered)
                {
                    throw new InvalidOperationException(
                        $"Benchmark proxy failed to load: {proxy.name}: {proxy.DisableReason}");
                }
            }
        }

        BasisLuauBenchmarkResult Measure(
            string id,
            string layer,
            string workload,
            int warmup,
            int iterations,
            int workloadUnits,
            IReadOnlyList<LuauState> rootStates,
            Action body,
            int hostCount = 0,
            int proxyCount = 0,
            string topology = null,
            string cohort = null,
            string[] tags = null)
        {
            if (iterations < 100)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations), "At least 100 samples are required.");
            }

            for (int i = 0; i < warmup; i++)
            {
                body();
            }

            Sample before = CaptureSample(rootStates);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var samples = new double[iterations];
            long frequency = Stopwatch.Frequency;
            for (int i = 0; i < iterations; i++)
            {
                long start = Stopwatch.GetTimestamp();
                body();
                samples[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / frequency;
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Sample after = CaptureSample(rootStates);
            Array.Sort(samples);

            double sum = 0;
            int budgetMisses = 0;
            int deadlineMisses = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double value = samples[i];
                sum += value;
                if (value > LuauBudgetMs)
                {
                    budgetMisses++;
                }

                if (value > Strict90HzDeadlineMs)
                {
                    deadlineMisses++;
                }
            }

            var result = new BasisLuauBenchmarkResult
            {
                id = id,
                layer = layer,
                workload = workload,
                iterations = iterations,
                warmup = warmup,
                workloadUnits = workloadUnits,
                meanMs = sum / iterations,
                p50Ms = BasisLuauBenchmarkStatistics.Percentile(samples, 0.50),
                p95Ms = BasisLuauBenchmarkStatistics.Percentile(samples, 0.95),
                p99Ms = BasisLuauBenchmarkStatistics.Percentile(samples, 0.99),
                maxMs = samples[samples.Length - 1],
                budgetMisses = budgetMisses,
                deadlineMisses = deadlineMisses,
                gcAllocBytesPerIter = allocatedBytes / (double)iterations,
                managedRetainedBytes = after.managedBytes - before.managedBytes,
                luauBytes = after.luauBytes,
                luauCapBytes = after.luauCapBytes,
                throughputUnitsPerSec = workloadUnits * 1000.0 / (sum / iterations),
                tags = tags ?? Array.Empty<string>(),
                hostCount = hostCount,
                proxyCount = proxyCount,
                topology = topology,
                cohort = cohort,
            };

            UnityEngine.Debug.Log(
                $"[BasisLuau.Benchmark] {id}: mean={result.meanMs:F3}ms p99={result.p99Ms:F3}ms " +
                $"max={result.maxMs:F3}ms alloc={result.gcAllocBytesPerIter:F1}B/iter");
            return result;
        }

        BasisLuauBenchmarkResult MeasureWithPhaseBreakdown(
            string id,
            string layer,
            string workload,
            int warmup,
            int iterations,
            int workloadUnits,
            IReadOnlyList<LuauState> rootStates,
            BenchmarkPopulation population,
            int hostCount,
            int proxyCount,
            string topology,
            string cohort,
            string[] tags)
        {
            if (iterations < 100)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations), "At least 100 samples are required.");
            }

            var phaseScratch = new double[LuauHostRuntimeBridge.DiagnosticPhaseCount];
            for (int i = 0; i < warmup; i++)
            {
                Array.Clear(phaseScratch, 0, phaseScratch.Length);
                population.PumpAllDiagnostic(1f / 90f, phaseScratch);
            }

            Sample before = CaptureSample(rootStates);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var totalSamples = new double[iterations];
            var phaseSamples = new double[LuauHostRuntimeBridge.DiagnosticPhaseCount][];
            for (int i = 0; i < phaseSamples.Length; i++)
            {
                phaseSamples[i] = new double[iterations];
            }

            long frequency = Stopwatch.Frequency;
            for (int sampleIndex = 0; sampleIndex < iterations; sampleIndex++)
            {
                Array.Clear(phaseScratch, 0, phaseScratch.Length);
                long start = Stopwatch.GetTimestamp();
                population.PumpAllDiagnostic(1f / 90f, phaseScratch);
                totalSamples[sampleIndex] = (Stopwatch.GetTimestamp() - start) * 1000.0 / frequency;
                for (int phaseIndex = 0; phaseIndex < phaseScratch.Length; phaseIndex++)
                {
                    phaseSamples[phaseIndex][sampleIndex] = phaseScratch[phaseIndex];
                }
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Sample after = CaptureSample(rootStates);
            Array.Sort(totalSamples);

            double sum = 0;
            int budgetMisses = 0;
            int deadlineMisses = 0;
            for (int i = 0; i < totalSamples.Length; i++)
            {
                double value = totalSamples[i];
                sum += value;
                if (value > LuauBudgetMs)
                {
                    budgetMisses++;
                }

                if (value > Strict90HzDeadlineMs)
                {
                    deadlineMisses++;
                }
            }

            double meanMs = sum / iterations;
            var phaseBreakdown = new BasisLuauBenchmarkPhaseBreakdown[phaseSamples.Length];
            for (int phaseIndex = 0; phaseIndex < phaseSamples.Length; phaseIndex++)
            {
                double[] samples = phaseSamples[phaseIndex];
                Array.Sort(samples);
                double phaseSum = 0;
                for (int i = 0; i < samples.Length; i++)
                {
                    phaseSum += samples[i];
                }

                double phaseMean = phaseSum / iterations;
                phaseBreakdown[phaseIndex] = new BasisLuauBenchmarkPhaseBreakdown
                {
                    phase = LuauHostRuntimeBridge.DiagnosticPhaseName(phaseIndex),
                    meanMs = phaseMean,
                    p95Ms = BasisLuauBenchmarkStatistics.Percentile(samples, 0.95),
                    p99Ms = BasisLuauBenchmarkStatistics.Percentile(samples, 0.99),
                    maxMs = samples[samples.Length - 1],
                    budgetSharePercent = meanMs <= 0 ? 0 : phaseMean * 100.0 / meanMs,
                };
            }

            var result = new BasisLuauBenchmarkResult
            {
                id = id,
                layer = layer,
                workload = workload,
                iterations = iterations,
                warmup = warmup,
                workloadUnits = workloadUnits,
                meanMs = meanMs,
                p50Ms = BasisLuauBenchmarkStatistics.Percentile(totalSamples, 0.50),
                p95Ms = BasisLuauBenchmarkStatistics.Percentile(totalSamples, 0.95),
                p99Ms = BasisLuauBenchmarkStatistics.Percentile(totalSamples, 0.99),
                maxMs = totalSamples[totalSamples.Length - 1],
                budgetMisses = budgetMisses,
                deadlineMisses = deadlineMisses,
                gcAllocBytesPerIter = allocatedBytes / (double)iterations,
                managedRetainedBytes = after.managedBytes - before.managedBytes,
                luauBytes = after.luauBytes,
                luauCapBytes = after.luauCapBytes,
                throughputUnitsPerSec = workloadUnits * 1000.0 / meanMs,
                tags = tags ?? Array.Empty<string>(),
                hostCount = hostCount,
                proxyCount = proxyCount,
                topology = topology,
                cohort = cohort,
                phaseBreakdown = phaseBreakdown,
            };

            UnityEngine.Debug.Log(
                $"[BasisLuau.Benchmark] {id}: mean={result.meanMs:F3}ms p99={result.p99Ms:F3}ms " +
                $"max={result.maxMs:F3}ms phases={result.phaseBreakdown.Length}");
            return result;
        }

        static BasisLuauBenchmarkResult CloneResult(
            BasisLuauBenchmarkResult source,
            string id,
            string layer,
            string topology)
        {
            return new BasisLuauBenchmarkResult
            {
                id = id,
                layer = layer,
                workload = source.workload,
                iterations = source.iterations,
                warmup = source.warmup,
                workloadUnits = source.workloadUnits,
                meanMs = source.meanMs,
                p50Ms = source.p50Ms,
                p95Ms = source.p95Ms,
                p99Ms = source.p99Ms,
                maxMs = source.maxMs,
                budgetMisses = source.budgetMisses,
                deadlineMisses = source.deadlineMisses,
                gcAllocBytesPerIter = source.gcAllocBytesPerIter,
                managedRetainedBytes = source.managedRetainedBytes,
                luauBytes = source.luauBytes,
                luauCapBytes = source.luauCapBytes,
                throughputUnitsPerSec = source.throughputUnitsPerSec,
                tags = new[] { "topology", topology, source.workload, "alias-of-scenario" },
                hostCount = source.hostCount,
                proxyCount = source.proxyCount,
                topology = topology,
                cohort = source.cohort,
            };
        }

        static unsafe Sample CaptureSample(IReadOnlyList<LuauState> rootStates)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            ulong luauBytes = 0;
            ulong luauCap = 0;
            for (int i = 0; i < rootStates.Count; i++)
            {
                LuauState state = rootStates[i];
                if (state == null)
                {
                    throw new InvalidOperationException($"Root state {i} is null.");
                }

                IntPtr ptr = (IntPtr)state.AsPointer();
                luauBytes += NativeTotalBytes(ptr);
                luauCap += NativeMemoryCap(ptr);
            }

            return new Sample
            {
                managedBytes = GC.GetTotalMemory(false),
                profilerBytes = Profiler.GetTotalAllocatedMemoryLong(),
                luauBytes = luauBytes,
                luauCapBytes = luauCap,
            };
        }

        static void PublishSnapshotBatch(BasisLuauNativeRuntime native, uint count)
        {
            Require(native.BeginSnapshotPublish(out ulong epoch), "snapshot publish begin failed");
            unsafe
            {
                for (uint i = 1; i <= count; i++)
                {
                    var slot = new BasisLuauSnapshotSlotNative
                    {
                        HandleIndex = i,
                        HandleGeneration = 1,
                        HostId = 1,
                    };
                    slot.Position[0] = i;
                    slot.Position[1] = i * 2;
                    slot.Position[2] = i * 3;
                    slot.Rotation[3] = 1;
                    native.WriteSnapshotSlot(i, ref slot);
                }
            }

            native.EndSnapshotPublish(epoch);
        }

        static unsafe void AllocateReadReleaseBuffer(BasisLuauNativeRuntime native, int payloadBytes)
        {
            Require(
                native.TryAllocBuffer(1, (uint)payloadBytes, out BasisLuauBufferRefNative bufferRef, out IntPtr ptr),
                $"buffer alloc failed for {payloadBytes} bytes");
            try
            {
                byte* bytes = (byte*)ptr;
                for (int i = 0; i < payloadBytes; i++)
                {
                    bytes[i] = (byte)(i & 0x7F);
                }

                Require(native.TryGetBufferBytes(ref bufferRef, out byte* readBytes, out uint length) != 0, "buffer get failed");
                Require(readBytes != null && length == payloadBytes, "buffer get returned invalid payload");
                GC.KeepAlive((IntPtr)readBytes);
            }
            finally
            {
                native.ReleaseBuffer(ref bufferRef);
            }
        }

        static BasisLuauCommandNative CreateCommand(LuauCommandType type, ushort hostId) => new()
        {
            Type = (ushort)type,
            HostId = hostId,
        };

        static void AddResult(BasisLuauBenchmarkReport report, BasisLuauBenchmarkResult result) =>
            report.results.Add(result);

        GameObject CreateOwnedObject(string objectName)
        {
            var go = new GameObject(objectName);
            _ownedObjects.Add(go);
            return go;
        }

        void DestroyOwnedObjects()
        {
            for (int i = _ownedObjects.Count - 1; i >= 0; i--)
            {
                if (_ownedObjects[i] != null)
                {
                    Destroy(_ownedObjects[i]);
                }
            }

            _ownedObjects.Clear();
        }

        void WriteArtifacts(BasisLuauBenchmarkReport report)
        {
            string dir = Path.Combine(Application.persistentDataPath, OutputFolderName);
            Directory.CreateDirectory(dir);

            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(dir, "results.json"), json, Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "summary.md"), BasisLuauBenchmarkMarkdown.Write(report), Encoding.UTF8);
            UnityEngine.Debug.Log($"[BasisLuau.Benchmark] wrote artifacts to {dir}");
        }

        BasisLuauBenchmarkResult TryLoadBaselinePrimary(string primaryCaseId)
        {
            string path = Path.Combine(Application.persistentDataPath, OutputFolderName, "baseline.json");
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                BasisLuauBenchmarkReport baseline = JsonUtility.FromJson<BasisLuauBenchmarkReport>(File.ReadAllText(path, Encoding.UTF8));
                if (baseline?.results == null)
                {
                    return null;
                }

                for (int i = 0; i < baseline.results.Count; i++)
                {
                    if (baseline.results[i].id == primaryCaseId)
                    {
                        return baseline.results[i];
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[BasisLuau.Benchmark] could not read baseline: {ex.Message}");
            }

            return null;
        }

        void WriteFailureArtifact(string failure)
        {
            string dir = Path.Combine(Application.persistentDataPath, OutputFolderName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "failure.txt"), failure, Encoding.UTF8);
        }

        static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

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
        if HANDLE == 0 and __luau_handles and __luau_handles[1] then
            HANDLE = __luau_handles[1]
        end
        local now = basis_time.now()
        if HANDLE ~= 0 and #now > 0 then
            transform.rotate(HANDLE, 0, 30 * dt, 0)
        end
    end,
}";

        static string LargePayloadSource(int payloadBytes)
        {
            string payload = new string('x', payloadBytes);
            return @"
local HANDLE = 0
local PAYLOAD = '" + payload + @"'
return {
    start = function()
        if __luau_handles and __luau_handles[1] then
            HANDLE = __luau_handles[1]
        end
    end,
    update = function(dt)
        if HANDLE == 0 and __luau_handles and __luau_handles[1] then
            HANDLE = __luau_handles[1]
        end
        if HANDLE ~= 0 then
            basis_ui.setText(HANDLE, PAYLOAD)
        end
    end,
}";
        }

#if (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR
        const string DllName = "__Internal";
#else
        const string DllName = "basis_luau_runtime";
#endif

        [DllImport(DllName, EntryPoint = "basis_luau_total_bytes", CallingConvention = CallingConvention.Cdecl)]
        static extern ulong NativeTotalBytes(IntPtr luaState);

        [DllImport(DllName, EntryPoint = "basis_luau_memory_cap", CallingConvention = CallingConvention.Cdecl)]
        static extern ulong NativeMemoryCap(IntPtr luaState);

        struct Sample
        {
            public long managedBytes;
            public long profilerBytes;
            public ulong luauBytes;
            public ulong luauCapBytes;
        }

        sealed class BenchmarkNativeHost : IDisposable
        {
            public readonly BasisLuauNativeRuntime Native;
            public readonly List<LuauState> RootStates;

            public BenchmarkNativeHost(BasisLuauNativeRuntime native, LuauState rootState)
            {
                Native = native;
                RootStates = new List<LuauState> { rootState };
            }

            public void Dispose() => Native.Dispose();
        }

        sealed class BenchmarkPopulation : IDisposable
        {
            readonly List<GameObject> _objects = new();
            readonly List<LuauPropHost> _hosts = new();

            public readonly List<LuauScriptProxy> Proxies = new();
            public readonly List<LuauState> RootStates = new();

            public void AddHost(GameObject hostObject, LuauPropHost host)
            {
                _objects.Add(hostObject);
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

            public void PumpAllDiagnostic(float deltaTime, double[] phaseMilliseconds)
            {
                for (int i = 0; i < _hosts.Count; i++)
                {
                    _hosts[i].RuntimeBridge.PumpUpdateDiagnostic(deltaTime, phaseMilliseconds);
                }
            }

            public void Dispose()
            {
                for (int i = _objects.Count - 1; i >= 0; i--)
                {
                    if (_objects[i] != null)
                    {
                        Destroy(_objects[i]);
                    }
                }

                _objects.Clear();
            }
        }
    }

    public static class BasisLuauBenchmarkPackageInfo
    {
        public const string Version = "0.2.0";
    }

    [Serializable]
    public sealed class BasisLuauBenchmarkSettings
    {
        public int nativeWarmup;
        public int nativeIterations;
        public int microWarmup;
        public int microIterations;
        public int frameWarmup;
        public int frameSamples;
        public int bridgeProxyCount;
        public int heavyScenarioHosts;
        public int[] largePayloadBytes;
        public int[] scaleHosts;
        public int[] singleHostProxyCounts;
        public string workloadHash;

        public int NativeWarmup => nativeWarmup;
        public int NativeIterations => nativeIterations;
        public int MicroWarmup => microWarmup;
        public int MicroIterations => microIterations;
        public int FrameWarmup => frameWarmup;
        public int FrameSamples => frameSamples;
        public int BridgeProxyCount => bridgeProxyCount;
        public int HeavyScenarioHosts => heavyScenarioHosts;
        public int[] LargePayloadBytes => largePayloadBytes;
        public int[] ScaleHosts => scaleHosts;
        public int[] SingleHostProxyCounts => singleHostProxyCounts;
        public string WorkloadHash => workloadHash;

        public static BasisLuauBenchmarkSettings For(BasisLuauBenchmarkProfile profile)
        {
            BasisLuauBenchmarkSettings settings = profile == BasisLuauBenchmarkProfile.Full
                ? new BasisLuauBenchmarkSettings
                {
                    nativeWarmup = 1000,
                    nativeIterations = 100000,
                    microWarmup = 100,
                    microIterations = 10000,
                    frameWarmup = 100,
                    frameSamples = 10000,
                    bridgeProxyCount = 128,
                    heavyScenarioHosts = 1000,
                    largePayloadBytes = new[] { 1024, 4096, 16384 },
                    scaleHosts = new[] { 32, 128, 512, 1000 },
                    singleHostProxyCounts = new[] { 32, 128, 256 },
                }
                : new BasisLuauBenchmarkSettings
                {
                    nativeWarmup = 100,
                    nativeIterations = 10000,
                    microWarmup = 10,
                    microIterations = 1000,
                    frameWarmup = 10,
                    frameSamples = 300,
                    bridgeProxyCount = 16,
                    heavyScenarioHosts = 32,
                    largePayloadBytes = new[] { 1024 },
                    scaleHosts = new[] { 32 },
                    singleHostProxyCounts = new[] { 32 },
                };

            settings.workloadHash = BasisLuauBenchmarkHash.Compute(settings.WorkloadDescriptor());
            return settings;
        }

        string WorkloadDescriptor() =>
            $"{nativeIterations}:{microIterations}:{frameSamples}:{bridgeProxyCount}:{heavyScenarioHosts}:" +
            $"{string.Join(",", largePayloadBytes)}:{string.Join(",", scaleHosts)}:{string.Join(",", singleHostProxyCounts)}";
    }

    [Serializable]
    public sealed class BasisLuauBenchmarkReport
    {
        public int schemaVersion;
        public string generatedUtc;
        public string profile;
        public string workloadHash;
        public BasisLuauBenchmarkEnvironment environment;
        public BasisLuauBenchmarkAcceptance acceptance;
        public List<BasisLuauBenchmarkResult> results = new();
    }

    [Serializable]
    public sealed class BasisLuauBenchmarkEnvironment
    {
        public string unityVersion;
        public string platform;
        public string scriptingBackend;
        public bool il2cpp;
        public bool developmentBuild;
        public string cpu;
        public int coreCount;
        public int systemMemory;
        public string graphicsDevice;
        public bool incrementalGc;
        public string packageVersion;
        public string gitCommit;
        public string nativeDllHash;
    }

    [Serializable]
    public sealed class BasisLuauBenchmarkResult
    {
        public string id;
        public string layer;
        public string workload;
        public int iterations;
        public int warmup;
        public int workloadUnits;
        public double meanMs;
        public double p50Ms;
        public double p95Ms;
        public double p99Ms;
        public double maxMs;
        public int budgetMisses;
        public int deadlineMisses;
        public double gcAllocBytesPerIter;
        public long managedRetainedBytes;
        public ulong luauBytes;
        public ulong luauCapBytes;
        public double throughputUnitsPerSec;
        public string[] tags;
        public int hostCount;
        public int proxyCount;
        public string topology;
        public string cohort;
        public BasisLuauBenchmarkPhaseBreakdown[] phaseBreakdown;
    }

    [Serializable]
    public sealed class BasisLuauBenchmarkPhaseBreakdown
    {
        public string phase;
        public double meanMs;
        public double p95Ms;
        public double p99Ms;
        public double maxMs;
        public double budgetSharePercent;
    }

    [Serializable]
    public sealed class BasisLuauBenchmarkAcceptance
    {
        public string primaryCaseId;
        public double p99BudgetMs;
        public double deadlineMs;
        public double baselineRegressionPercent;
        public bool baselineCompared;
        public bool passed;
        public string message;

        public static BasisLuauBenchmarkAcceptance Default() => new()
        {
            primaryCaseId = "scenario.mixed-ugc-1000",
            p99BudgetMs = 2.0,
            deadlineMs = 1000.0 / 90.0,
            baselineRegressionPercent = 5.0,
            baselineCompared = false,
            passed = false,
            message = "not evaluated",
        };

        public void Evaluate(List<BasisLuauBenchmarkResult> results, BasisLuauBenchmarkResult baseline = null)
        {
            BasisLuauBenchmarkResult primary = null;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].id == primaryCaseId)
                {
                    primary = results[i];
                    break;
                }
            }

            if (primary == null)
            {
                passed = false;
                message = $"primary case '{primaryCaseId}' was not produced";
                return;
            }

            bool p99Ok = primary.p99Ms <= p99BudgetMs;
            bool budgetOk = primary.budgetMisses == 0;
            bool deadlineOk = primary.deadlineMisses == 0 && primary.maxMs <= deadlineMs;
            bool baselineOk = true;
            baselineCompared = baseline != null;
            if (baselineCompared)
            {
                double multiplier = 1.0 + baselineRegressionPercent / 100.0;
                baselineOk = primary.p99Ms <= baseline.p99Ms * multiplier &&
                    primary.luauBytes <= baseline.luauBytes * multiplier;
            }

            passed = p99Ok && budgetOk && deadlineOk && baselineOk;
            message = passed
                ? baselineCompared ? "passed with baseline" : "passed without baseline"
                : $"failed: p99={primary.p99Ms:F3}ms budgetMisses={primary.budgetMisses} " +
                  $"deadlineMisses={primary.deadlineMisses} max={primary.maxMs:F3}ms " +
                  $"baselineCompared={baselineCompared}";
        }
    }

    static class BasisLuauBenchmarkMarkdown
    {
        public static string Write(BasisLuauBenchmarkReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Basis Luau IL2CPP Benchmark Summary");
            sb.AppendLine();
            sb.AppendLine($"- Generated: {report.generatedUtc}");
            sb.AppendLine($"- Profile: {report.profile}");
            sb.AppendLine($"- Workload hash: `{report.workloadHash}`");
            sb.AppendLine($"- Acceptance: {(report.acceptance.passed ? "PASS" : "FAIL")} - {report.acceptance.message}");
            sb.AppendLine();
            sb.AppendLine("## Environment");
            sb.AppendLine();
            sb.AppendLine($"- Unity: {report.environment.unityVersion}");
            sb.AppendLine($"- Platform: {report.environment.platform}");
            sb.AppendLine($"- Scripting backend: {report.environment.scriptingBackend}");
            sb.AppendLine($"- Development build: {report.environment.developmentBuild}");
            sb.AppendLine($"- CPU: {report.environment.cpu} ({report.environment.coreCount} cores)");
            sb.AppendLine($"- Memory: {report.environment.systemMemory} MiB");
            sb.AppendLine($"- Graphics: {report.environment.graphicsDevice}");
            sb.AppendLine($"- Incremental GC: {report.environment.incrementalGc}");
            sb.AppendLine($"- Package: {report.environment.packageVersion}");
            sb.AppendLine($"- Git commit: {report.environment.gitCommit}");
            sb.AppendLine($"- Native runtime hash: {report.environment.nativeDllHash}");
            sb.AppendLine();
            AppendAnalysis(sb, report);
            sb.AppendLine("## Results");
            sb.AppendLine();
            sb.AppendLine("| Case | Layer | Workload | Topology | Cohort | Hosts | Proxies | Iter | Mean ms | p95 ms | p99 ms | Max ms | Budget misses | Deadline misses | GC B/iter | Luau bytes |");
            sb.AppendLine("|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < report.results.Count; i++)
            {
                BasisLuauBenchmarkResult r = report.results[i];
                sb.AppendLine(
                    $"| `{r.id}` | {r.layer} | {r.workload} | {ValueOrDash(r.topology)} | {ValueOrDash(r.cohort)} | " +
                    $"{r.hostCount} | {r.proxyCount} | {r.iterations} | {r.meanMs:F3} | " +
                    $"{r.p95Ms:F3} | {r.p99Ms:F3} | {r.maxMs:F3} | {r.budgetMisses} | " +
                    $"{r.deadlineMisses} | {r.gcAllocBytesPerIter:F1} | {r.luauBytes} |");
            }

            return sb.ToString();
        }

        static void AppendAnalysis(StringBuilder sb, BasisLuauBenchmarkReport report)
        {
            BasisLuauBenchmarkResult primary = Find(report, "scenario.mixed-ugc-1000");
            BasisLuauBenchmarkResult diagnostic = FindFirstWithPrefix(report, "diagnostic.phase-mixed-ugc-");
            sb.AppendLine("## Analysis");
            sb.AppendLine();
            if (primary != null)
            {
                sb.AppendLine($"- Primary scenario p99: {primary.p99Ms:F3} ms ({primary.budgetMisses} budget misses, {primary.deadlineMisses} deadline misses).");
            }

            string dominant = InferDominantCause(report, diagnostic);
            sb.AppendLine($"- Dominant candidate: {dominant}.");
            sb.AppendLine();

            AppendTopologySummary(sb, report);
            AppendCohortSummary(sb, report);
            AppendPhaseSummary(sb, diagnostic);
        }

        static void AppendTopologySummary(StringBuilder sb, BasisLuauBenchmarkReport report)
        {
            sb.AppendLine("### Topology");
            sb.AppendLine();
            sb.AppendLine("| Proxies | Single-host p99 ms | Multi-host p99 ms | Multi / single |");
            sb.AppendLine("|---:|---:|---:|---:|");
            int[] counts = { 32, 128, 256, 512, 1000 };
            for (int i = 0; i < counts.Length; i++)
            {
                int count = counts[i];
                BasisLuauBenchmarkResult single = Find(report, $"topology.single-host-mixed-{count}");
                BasisLuauBenchmarkResult multi = Find(report, $"topology.multi-host-mixed-{count}");
                if (single == null && multi == null)
                {
                    continue;
                }

                string ratio = single != null && multi != null && single.p99Ms > 0
                    ? (multi.p99Ms / single.p99Ms).ToString("F2")
                    : "-";
                sb.AppendLine($"| {count} | {FormatMs(single)} | {FormatMs(multi)} | {ratio} |");
            }

            sb.AppendLine();
        }

        static void AppendCohortSummary(StringBuilder sb, BasisLuauBenchmarkReport report)
        {
            sb.AppendLine("### Cohorts");
            sb.AppendLine();
            sb.AppendLine("| Cohort | 32 p99 | 128 p99 | 512 p99 | 1000 p99 |");
            sb.AppendLine("|---|---:|---:|---:|---:|");
            string[] cohorts = { "dormant", "light", "compute", "bridge", "large-payload" };
            for (int i = 0; i < cohorts.Length; i++)
            {
                string cohort = cohorts[i];
                sb.AppendLine(
                    $"| {cohort} | {FormatMs(Find(report, $"scenario.cohort-{cohort}-32"))} | " +
                    $"{FormatMs(Find(report, $"scenario.cohort-{cohort}-128"))} | " +
                    $"{FormatMs(Find(report, $"scenario.cohort-{cohort}-512"))} | " +
                    $"{FormatMs(Find(report, $"scenario.cohort-{cohort}-1000"))} |");
            }

            sb.AppendLine();
        }

        static void AppendPhaseSummary(StringBuilder sb, BasisLuauBenchmarkResult diagnostic)
        {
            if (diagnostic?.phaseBreakdown == null || diagnostic.phaseBreakdown.Length == 0)
            {
                return;
            }

            sb.AppendLine("### Pump Phases");
            sb.AppendLine();
            sb.AppendLine($"Diagnostic case: `{diagnostic.id}`");
            sb.AppendLine();
            sb.AppendLine("| Phase | Mean ms | p95 ms | p99 ms | Max ms | Mean share |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|");
            for (int i = 0; i < diagnostic.phaseBreakdown.Length; i++)
            {
                BasisLuauBenchmarkPhaseBreakdown phase = diagnostic.phaseBreakdown[i];
                sb.AppendLine(
                    $"| {phase.phase} | {phase.meanMs:F3} | {phase.p95Ms:F3} | {phase.p99Ms:F3} | " +
                    $"{phase.maxMs:F3} | {phase.budgetSharePercent:F1}% |");
            }

            sb.AppendLine();
        }

        static string InferDominantCause(BasisLuauBenchmarkReport report, BasisLuauBenchmarkResult diagnostic)
        {
            BasisLuauBenchmarkResult single128 = Find(report, "topology.single-host-mixed-128");
            BasisLuauBenchmarkResult multi128 = Find(report, "topology.multi-host-mixed-128");
            if (single128 != null && multi128 != null && single128.p99Ms > 0 && multi128.p99Ms / single128.p99Ms >= 2.0)
            {
                return "multi-host overhead dominant";
            }

            BasisLuauBenchmarkResult compute1000 = Find(report, "scenario.cohort-compute-1000");
            BasisLuauBenchmarkResult light1000 = Find(report, "scenario.cohort-light-1000");
            if (compute1000 != null && light1000 != null && compute1000.p99Ms >= light1000.p99Ms * 1.5)
            {
                return "compute cohort dominant";
            }

            if (diagnostic?.phaseBreakdown != null)
            {
                BasisLuauBenchmarkPhaseBreakdown phase = DominantPhase(diagnostic.phaseBreakdown);
                if (phase != null)
                {
                    return $"{phase.phase} phase dominant";
                }
            }

            return "undetermined";
        }

        static BasisLuauBenchmarkPhaseBreakdown DominantPhase(BasisLuauBenchmarkPhaseBreakdown[] phases)
        {
            BasisLuauBenchmarkPhaseBreakdown best = null;
            for (int i = 0; i < phases.Length; i++)
            {
                if (best == null || phases[i].meanMs > best.meanMs)
                {
                    best = phases[i];
                }
            }

            return best;
        }

        static BasisLuauBenchmarkResult Find(BasisLuauBenchmarkReport report, string id)
        {
            for (int i = 0; i < report.results.Count; i++)
            {
                if (report.results[i].id == id)
                {
                    return report.results[i];
                }
            }

            return null;
        }

        static BasisLuauBenchmarkResult FindFirstWithPrefix(BasisLuauBenchmarkReport report, string prefix)
        {
            for (int i = 0; i < report.results.Count; i++)
            {
                if (report.results[i].id.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return report.results[i];
                }
            }

            return null;
        }

        static string FormatMs(BasisLuauBenchmarkResult result) => result == null ? "-" : result.p99Ms.ToString("F3");
        static string ValueOrDash(string value) => string.IsNullOrEmpty(value) ? "-" : value;
    }

    public static class BasisLuauBenchmarkStatistics
    {
        public static double Percentile(double[] sortedSamples, double percentile)
        {
            if (sortedSamples == null || sortedSamples.Length == 0)
            {
                throw new ArgumentException("At least one sample is required.", nameof(sortedSamples));
            }

            if (percentile < 0 || percentile > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(percentile));
            }

            double index = (sortedSamples.Length - 1) * percentile;
            int lower = (int)index;
            int upper = Math.Min(lower + 1, sortedSamples.Length - 1);
            double fraction = index - lower;
            return sortedSamples[lower] + (sortedSamples[upper] - sortedSamples[lower]) * fraction;
        }
    }

    public static class BasisLuauBenchmarkPhaseNames
    {
        public static string[] All()
        {
            var names = new string[LuauHostRuntimeBridge.DiagnosticPhaseCount];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = LuauHostRuntimeBridge.DiagnosticPhaseName(i);
            }

            return names;
        }
    }

    public static class BasisLuauBenchmarkHash
    {
        public static string Compute(string text)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= 1099511628211UL;
                }

                return hash.ToString("x16");
            }
        }
    }
}
