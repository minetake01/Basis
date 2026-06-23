using Minetake.Basis.Luau.Benchmarks;
using NUnit.Framework;
using UnityEngine;

namespace Minetake.Basis.Luau.Tests
{
    public sealed class LuauPlayerBenchmarkReportTests
    {
        [Test]
        public void Percentile_InterpolatesSortedSamples()
        {
            double[] samples = { 1, 2, 3, 4, 5 };

            Assert.That(BasisLuauBenchmarkStatistics.Percentile(samples, 0.50), Is.EqualTo(3));
            Assert.That(BasisLuauBenchmarkStatistics.Percentile(samples, 0.95), Is.EqualTo(4.8).Within(0.0001));
        }

        [Test]
        public void Acceptance_FailsWhenPrimaryCaseMissing()
        {
            BasisLuauBenchmarkAcceptance acceptance = BasisLuauBenchmarkAcceptance.Default();

            acceptance.Evaluate(new System.Collections.Generic.List<BasisLuauBenchmarkResult>());

            Assert.That(acceptance.passed, Is.False);
            Assert.That(acceptance.message, Does.Contain("primary case"));
        }

        [Test]
        public void Acceptance_PassesPrimaryScenarioInsideGate()
        {
            BasisLuauBenchmarkAcceptance acceptance = BasisLuauBenchmarkAcceptance.Default();
            var results = new System.Collections.Generic.List<BasisLuauBenchmarkResult>
            {
                new()
                {
                    id = "scenario.mixed-ugc-1000",
                    p99Ms = 1.9,
                    maxMs = 2.0,
                    budgetMisses = 0,
                    deadlineMisses = 0,
                },
            };

            acceptance.Evaluate(results);

            Assert.That(acceptance.passed, Is.True);
        }

        [Test]
        public void WorkloadHash_IsStableForFullProfile()
        {
            BasisLuauBenchmarkSettings first = BasisLuauBenchmarkSettings.For(BasisLuauBenchmarkProfile.Full);
            BasisLuauBenchmarkSettings second = BasisLuauBenchmarkSettings.For(BasisLuauBenchmarkProfile.Full);

            Assert.That(first.WorkloadHash, Is.EqualTo(second.WorkloadHash));
            Assert.That(first.FrameSamples, Is.EqualTo(10000));
            Assert.That(System.Array.IndexOf(first.ScaleHosts, 1000), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void ReportJson_ContainsEnvironmentAndResults()
        {
            var report = new BasisLuauBenchmarkReport
            {
                schemaVersion = 2,
                generatedUtc = "2026-06-23T00:00:00.0000000Z",
                profile = BasisLuauBenchmarkProfile.Smoke.ToString(),
                workloadHash = "abc",
                environment = new BasisLuauBenchmarkEnvironment
                {
                    unityVersion = "test",
                    platform = "WindowsPlayer",
                    scriptingBackend = "IL2CPP",
                    il2cpp = true,
                },
                acceptance = BasisLuauBenchmarkAcceptance.Default(),
            };
            report.results.Add(new BasisLuauBenchmarkResult { id = "native.command-ring-small", layer = "Native" });

            string json = JsonUtility.ToJson(report, true);

            Assert.That(json, Does.Contain("environment"));
            Assert.That(json, Does.Contain("native.command-ring-small"));
            Assert.That(json, Does.Contain("IL2CPP"));
        }

        [Test]
        public void DiagnosticPhaseNames_AreFixedOrder()
        {
            string[] names = BasisLuauBenchmarkPhaseNames.All();

            Assert.That(names, Is.EqualTo(new[]
            {
                "flushCommandsBefore",
                "processTickets",
                "publishSnapshots",
                "drainEvents",
                "kickScheduler",
                "flushCommandsAfter",
            }));
        }

        [Test]
        public void ReportJson_ContainsPhaseBreakdownAndMetadata()
        {
            var report = new BasisLuauBenchmarkReport
            {
                schemaVersion = 2,
                generatedUtc = "2026-06-23T00:00:00.0000000Z",
                profile = BasisLuauBenchmarkProfile.Smoke.ToString(),
                workloadHash = "abc",
                environment = new BasisLuauBenchmarkEnvironment(),
                acceptance = BasisLuauBenchmarkAcceptance.Default(),
            };
            report.results.Add(new BasisLuauBenchmarkResult
            {
                id = "diagnostic.phase-mixed-ugc-32",
                layer = "Diagnostic",
                topology = "multi-host",
                cohort = "mixed",
                tags = new[] { "diagnostic", "phase" },
                phaseBreakdown = new[]
                {
                    new BasisLuauBenchmarkPhaseBreakdown
                    {
                        phase = "kickScheduler",
                        meanMs = 1.0,
                        p95Ms = 1.2,
                        p99Ms = 1.3,
                        maxMs = 1.4,
                        budgetSharePercent = 80.0,
                    },
                },
            });

            string json = JsonUtility.ToJson(report, true);

            Assert.That(json, Does.Contain("phaseBreakdown"));
            Assert.That(json, Does.Contain("kickScheduler"));
            Assert.That(json, Does.Contain("multi-host"));
            Assert.That(json, Does.Contain("diagnostic"));
        }

        [Test]
        public void PhaseBreakdown_CanSumCloseToTotal()
        {
            var phases = new[]
            {
                new BasisLuauBenchmarkPhaseBreakdown { meanMs = 0.25 },
                new BasisLuauBenchmarkPhaseBreakdown { meanMs = 0.75 },
            };
            var result = new BasisLuauBenchmarkResult
            {
                meanMs = 1.01,
                phaseBreakdown = phases,
            };

            double sum = 0;
            for (int i = 0; i < result.phaseBreakdown.Length; i++)
            {
                sum += result.phaseBreakdown[i].meanMs;
            }

            Assert.That(sum, Is.EqualTo(result.meanMs).Within(0.02));
        }
    }
}
