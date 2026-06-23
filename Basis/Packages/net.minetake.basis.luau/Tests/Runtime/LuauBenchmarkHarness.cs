using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Luau;
using Luau.Unity;
using Minetake.Basis.Luau.Runtime;
using UnityEngine;
using UnityEngine.Profiling;

namespace Minetake.Basis.Luau.Tests
{
    internal static class LuauBenchmarkHarness
    {
        const string LogPrefix = "[BasisLuau.Benchmark]";

        [DllImport("basis_luau_runtime", EntryPoint = "basis_luau_total_bytes", CallingConvention = CallingConvention.Cdecl)]
        static extern ulong NativeTotalBytes(IntPtr luaState);

        [DllImport("basis_luau_runtime", EntryPoint = "basis_luau_memory_cap", CallingConvention = CallingConvention.Cdecl)]
        static extern ulong NativeMemoryCap(IntPtr luaState);

        public struct Sample
        {
            public long ManagedBytes;
            public long ProfilerAllocatedBytes;
            public ulong LuauBytes;
            public ulong LuauCapBytes;
        }

        public struct Result
        {
            public string Layer;
            public string Name;
            public int Iterations;
            public double TotalMs;
            public double AvgMicroseconds;
            public Sample Before;
            public Sample After;
            public string Notes;

            public long ManagedDelta => After.ManagedBytes - Before.ManagedBytes;
            public long LuauDelta => (long)(After.LuauBytes - Before.LuauBytes);
        }

        public struct DistributionResult
        {
            public string Layer;
            public string Name;
            public int Iterations;
            public int WorkloadUnits;
            public double MeanMs;
            public double P50Ms;
            public double P95Ms;
            public double P99Ms;
            public double MaxMs;
            public double BudgetMs;
            public double DeadlineMs;
            public int BudgetMisses;
            public int DeadlineMisses;
            public long AllocatedBytes;
            public Sample Before;
            public Sample After;
            public string Notes;

            public double BudgetMissPercent => BudgetMisses * 100.0 / Iterations;
            public double DeadlineMissPercent => DeadlineMisses * 100.0 / Iterations;
            public double UnitsPerSecond => WorkloadUnits * 1000.0 / MeanMs;
            public double AllocatedBytesPerIteration => AllocatedBytes / (double)Iterations;
            public long ManagedDelta => After.ManagedBytes - Before.ManagedBytes;
            public long LuauDelta => (long)(After.LuauBytes - Before.LuauBytes);
        }

        static readonly List<Result> SessionResults = new();
        static readonly List<DistributionResult> DistributionResults = new();
        static readonly bool ThreadAllocationCounterSupported = DetectThreadAllocationCounter();

        public static IReadOnlyList<Result> Results => SessionResults;

        public static void ClearSession()
        {
            SessionResults.Clear();
            DistributionResults.Clear();
        }

        public static Sample CaptureSample(LuauState rootState = null)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            ulong luauBytes = 0;
            ulong luauCap = 0;
            if (rootState != null)
            {
                try
                {
                    unsafe
                    {
                        IntPtr ptr = (IntPtr)rootState.AsPointer();
                        luauBytes = NativeTotalBytes(ptr);
                        luauCap = NativeMemoryCap(ptr);
                    }
                }
                catch (DllNotFoundException)
                {
                }
            }

            return new Sample
            {
                ManagedBytes = GC.GetTotalMemory(false),
                ProfilerAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(),
                LuauBytes = luauBytes,
                LuauCapBytes = luauCap,
            };
        }

        public static Sample CaptureAggregateSample(IReadOnlyList<LuauState> rootStates)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            ulong luauBytes = 0;
            ulong luauCap = 0;
            for (int i = 0; i < rootStates.Count; i++)
            {
                LuauState rootState = rootStates[i];
                if (rootState == null)
                {
                    throw new InvalidOperationException($"Root state {i} is null.");
                }

                unsafe
                {
                    IntPtr ptr = (IntPtr)rootState.AsPointer();
                    luauBytes += NativeTotalBytes(ptr);
                    luauCap += NativeMemoryCap(ptr);
                }
            }

            return new Sample
            {
                ManagedBytes = GC.GetTotalMemory(false),
                ProfilerAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(),
                LuauBytes = luauBytes,
                LuauCapBytes = luauCap,
            };
        }

        public static DistributionResult MeasureDistribution(
            string layer,
            string name,
            int warmup,
            int iterations,
            int workloadUnits,
            Action body,
            IReadOnlyList<LuauState> rootStates,
            double budgetMs,
            double deadlineMs,
            string notes = null)
        {
            if (iterations < 100)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations), "At least 100 samples are required for p99.");
            }

            if (workloadUnits <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(workloadUnits));
            }

            for (int i = 0; i < warmup; i++)
            {
                body();
            }

            Sample before = CaptureAggregateSample(rootStates);
            long allocatedBefore = ThreadAllocationCounterSupported ? GC.GetAllocatedBytesForCurrentThread() : 0;
            var samples = new double[iterations];
            long frequency = Stopwatch.Frequency;
            for (int i = 0; i < iterations; i++)
            {
                long start = Stopwatch.GetTimestamp();
                body();
                samples[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / frequency;
            }

            long allocatedBytes = ThreadAllocationCounterSupported
                ? GC.GetAllocatedBytesForCurrentThread() - allocatedBefore
                : -1;
            Sample after = CaptureAggregateSample(rootStates);
            Array.Sort(samples);

            double sum = 0;
            int budgetMisses = 0;
            int deadlineMisses = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double sample = samples[i];
                sum += sample;
                if (sample > budgetMs)
                {
                    budgetMisses++;
                }

                if (sample > deadlineMs)
                {
                    deadlineMisses++;
                }
            }

            var result = new DistributionResult
            {
                Layer = layer,
                Name = name,
                Iterations = iterations,
                WorkloadUnits = workloadUnits,
                MeanMs = sum / iterations,
                P50Ms = Percentile(samples, 0.50),
                P95Ms = Percentile(samples, 0.95),
                P99Ms = Percentile(samples, 0.99),
                MaxMs = samples[samples.Length - 1],
                BudgetMs = budgetMs,
                DeadlineMs = deadlineMs,
                BudgetMisses = budgetMisses,
                DeadlineMisses = deadlineMisses,
                AllocatedBytes = allocatedBytes,
                Before = before,
                After = after,
                Notes = notes ?? string.Empty,
            };

            DistributionResults.Add(result);
            LogDistribution(result);
            return result;
        }

        public static Result Measure(string layer, string name, int warmup, int iterations, Action body, LuauState rootState = null, string notes = null)
        {
            if (iterations <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations));
            }

            for (int i = 0; i < warmup; i++)
            {
                body();
            }

            Sample before = CaptureSample(rootState);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                body();
            }

            sw.Stop();
            Sample after = CaptureSample(rootState);

            var result = new Result
            {
                Layer = layer,
                Name = name,
                Iterations = iterations,
                TotalMs = sw.Elapsed.TotalMilliseconds,
                AvgMicroseconds = sw.Elapsed.TotalMilliseconds * 1000.0 / iterations,
                Before = before,
                After = after,
                Notes = notes ?? string.Empty,
            };

            SessionResults.Add(result);
            LogResult(result);
            return result;
        }

        public static void RecordFrameBenchmark(string layer, string name, int frames, double totalMs, Sample before, Sample after, string notes = null)
        {
            var result = new Result
            {
                Layer = layer,
                Name = name,
                Iterations = frames,
                TotalMs = totalMs,
                AvgMicroseconds = totalMs * 1000.0 / frames,
                Before = before,
                After = after,
                Notes = notes ?? string.Empty,
            };

            SessionResults.Add(result);
            LogResult(result);
        }

        public static void RequireNativeRuntime()
        {
            if (!BasisLuauNativeRuntime.IsAvailable)
            {
                throw new InvalidOperationException(
                    $"Native runtime required: {BasisLuauNativeRuntime.UnavailableReason}");
            }
        }

        public static byte[] CompileSigned(string source)
        {
            byte[] bytecode = LuauCompiler.Compile(Encoding.UTF8.GetBytes(source));
            return LuauBytecodeSigner.AttachSignature(bytecode);
        }

        public static void LogSessionSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{LogPrefix} === session summary ({SessionResults.Count + DistributionResults.Count} benchmarks) ===");
            for (int i = 0; i < SessionResults.Count; i++)
            {
                Result r = SessionResults[i];
                sb.AppendLine(
                    $"{LogPrefix} [{r.Layer}] {r.Name}: {r.TotalMs:F3}ms total, {r.AvgMicroseconds:F2}us/iter, " +
                    $"managedDelta={FormatBytes(r.ManagedDelta)}, luau={FormatBytes((long)r.After.LuauBytes)}/{FormatBytes((long)r.After.LuauCapBytes)} {r.Notes}");
            }

            for (int i = 0; i < DistributionResults.Count; i++)
            {
                DistributionResult r = DistributionResults[i];
                sb.AppendLine(
                    $"{LogPrefix} [{r.Layer}] {r.Name}: mean={r.MeanMs:F3}ms p50={r.P50Ms:F3}ms " +
                    $"p95={r.P95Ms:F3}ms p99={r.P99Ms:F3}ms max={r.MaxMs:F3}ms, " +
                    $"budgetMiss={r.BudgetMissPercent:F1}% deadlineMiss={r.DeadlineMissPercent:F1}%, " +
                    $"throughput={r.UnitsPerSecond:F0} units/s, gcAlloc={FormatAllocatedBytes(r.AllocatedBytesPerIteration)}, " +
                    $"luau={FormatBytes((long)r.After.LuauBytes)}/{FormatBytes((long)r.After.LuauCapBytes)} {r.Notes}");
            }

            string text = sb.ToString();
            UnityEngine.Debug.Log(text);
            try
            {
                string path = System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Temp/luau-benchmark-results.txt");
                System.IO.File.AppendAllText(path, text + System.Environment.NewLine);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"{LogPrefix} Could not write results file: {ex.Message}");
            }
        }

        public static void LogEnvironment()
        {
            UnityEngine.Debug.Log(
                $"{LogPrefix} environment: unity={Application.unityVersion}, editor={Application.isEditor}, " +
                $"platform={Application.platform}, cpu='{SystemInfo.processorType}', cores={SystemInfo.processorCount}, " +
                $"memory={SystemInfo.systemMemorySize}MiB, graphics='{SystemInfo.graphicsDeviceName}', " +
                $"threadAllocCounter={ThreadAllocationCounterSupported}");
        }

        static void LogResult(Result result)
        {
            UnityEngine.Debug.Log(
                $"{LogPrefix} [{result.Layer}] {result.Name}: " +
                $"{result.TotalMs:F3}ms / {result.Iterations} iter ({result.AvgMicroseconds:F2}us/iter), " +
                $"managedDelta={FormatBytes(result.ManagedDelta)}, profiler={FormatBytes(result.After.ProfilerAllocatedBytes)}, " +
                $"luau={FormatBytes((long)result.After.LuauBytes)}/{FormatBytes((long)result.After.LuauCapBytes)} {result.Notes}");
        }

        static void LogDistribution(DistributionResult result)
        {
            UnityEngine.Debug.Log(
                $"{LogPrefix} [{result.Layer}] {result.Name}: {result.Iterations} samples, " +
                $"mean={result.MeanMs:F3}ms, p50={result.P50Ms:F3}ms, p95={result.P95Ms:F3}ms, " +
                $"p99={result.P99Ms:F3}ms, max={result.MaxMs:F3}ms, " +
                $"budget>{result.BudgetMs:F3}ms={result.BudgetMissPercent:F1}%, " +
                $"deadline>{result.DeadlineMs:F3}ms={result.DeadlineMissPercent:F1}%, " +
                $"throughput={result.UnitsPerSecond:F0} units/s, " +
                $"gcAlloc={FormatAllocatedBytes(result.AllocatedBytesPerIteration)}, " +
                $"managedRetained={FormatBytes(result.ManagedDelta)}, " +
                $"luau={FormatBytes((long)result.After.LuauBytes)}/{FormatBytes((long)result.After.LuauCapBytes)} " +
                result.Notes);
        }

        static double Percentile(double[] sortedSamples, double percentile)
        {
            double index = (sortedSamples.Length - 1) * percentile;
            int lower = (int)index;
            int upper = Math.Min(lower + 1, sortedSamples.Length - 1);
            double fraction = index - lower;
            return sortedSamples[lower] + (sortedSamples[upper] - sortedSamples[lower]) * fraction;
        }

        public static bool TryGetThreadAllocatedBytes(out long bytes)
        {
            bytes = ThreadAllocationCounterSupported ? GC.GetAllocatedBytesForCurrentThread() : 0;
            return ThreadAllocationCounterSupported;
        }

        static bool DetectThreadAllocationCounter()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            byte[] probe = new byte[1024];
            GC.KeepAlive(probe);
            return GC.GetAllocatedBytesForCurrentThread() - before >= probe.Length;
        }

        static string FormatAllocatedBytes(double bytes) =>
            bytes < 0 ? "unavailable" : $"{FormatBytes((long)bytes)}/iter";

        public static string FormatBytesPublic(long bytes) => FormatBytes(bytes);

        static string FormatBytes(long bytes)
        {
            if (bytes < 0)
            {
                return $"-{FormatBytes(-bytes)}";
            }

            if (bytes >= 1024 * 1024)
            {
                return $"{bytes / (1024.0 * 1024.0):F2} MiB";
            }

            if (bytes >= 1024)
            {
                return $"{bytes / 1024.0:F1} KiB";
            }

            return $"{bytes} B";
        }
    }
}
