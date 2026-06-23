using Luau.Unity;
using Minetake.Basis.Luau.Registry;
using Minetake.Basis.Luau.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Minetake.Basis.Luau.Tests
{
    [Category("Benchmark")]
    public sealed class LuauUnityBenchmarkTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp() => LuauBenchmarkHarness.ClearSession();

        [OneTimeTearDown]
        public void OneTimeTearDown() => LuauBenchmarkHarness.LogSessionSummary();

        [Test]
        public void CommandCatalog_IsRegistered()
        {
            LuauBenchmarkHarness.Measure(
                "Unity",
                "CommandCatalog.IsRegistered",
                warmup: 1000,
                iterations: 100_000,
                () =>
                {
                    LuauCommandCatalog.IsRegistered(LuauCommandType.SetPosition);
                    LuauCommandCatalog.IsRegistered(LuauCommandType.Rotate);
                    LuauCommandCatalog.IsRegistered(LuauCommandType.TicketClone);
                });
        }

        [Test]
        public void AuthorityTable_RegisterAndResolve()
        {
            var hostGo = new GameObject("bench-authority-host");
            var authority = new LuauAuthorityTable(1, hostGo.transform, LuauHostKind.Prop);
            var target = new GameObject("bench-target").transform;
            LuauObjectHandle handle = default;

            try
            {
                LuauBenchmarkHarness.Measure(
                    "Unity",
                    "AuthorityTable.Register+Resolve",
                    warmup: 100,
                    iterations: 10_000,
                    () =>
                    {
                        handle = authority.Register(target);
                        authority.TryResolve(handle, out _);
                    },
                    notes: "single transform slot");
            }
            finally
            {
                Object.DestroyImmediate(target.gameObject);
                Object.DestroyImmediate(hostGo);
            }
        }

        [Test]
        public void AuthorityTable_ManyTransformSnapshots()
        {
            const int transformCount = 128;
            var hostGo = new GameObject("bench-many-transforms");
            var authority = new LuauAuthorityTable(1, hostGo.transform, LuauHostKind.Prop);
            var transforms = new Transform[transformCount];
            var handles = new LuauObjectHandle[transformCount];

            try
            {
                for (int i = 0; i < transformCount; i++)
                {
                    var child = new GameObject($"t{i}").transform;
                    child.SetParent(hostGo.transform);
                    transforms[i] = child;
                    handles[i] = authority.Register(child);
                }

                LuauBenchmarkHarness.Measure(
                    "Unity",
                    $"AuthorityTable.Resolve({transformCount} transforms)",
                    warmup: 10,
                    iterations: 1_000,
                    () =>
                    {
                        for (int i = 0; i < transformCount; i++)
                        {
                            authority.TryResolve(handles[i], out _);
                        }
                    },
                    notes: $"{transformCount} registered transforms");
            }
            finally
            {
                for (int i = 0; i < transformCount; i++)
                {
                    if (transforms[i] != null)
                    {
                        Object.DestroyImmediate(transforms[i].gameObject);
                    }
                }

                Object.DestroyImmediate(hostGo);
            }
        }

        [Test]
        public void CommandFlush_SetPosition()
        {
            var hostGo = new GameObject("bench-flush-host");
            var host = hostGo.AddComponent<LuauPropHost>();
            var authority = new LuauAuthorityTable(1, hostGo.transform, LuauHostKind.Prop);
            var tickets = new LuauTicketProcessor(host, authority, new LuauTicketRegistry());
            var proxyGenerations = new System.Collections.Generic.Dictionary<uint, LuauScriptProxy.ProxyGenerationState>
            {
                [1] = new LuauScriptProxy.ProxyGenerationState { Generation = 1, Disabled = false },
            };
            LuauObjectHandle handle = authority.Register(hostGo.transform);

            try
            {
                LuauBenchmarkHarness.Measure(
                    "Unity",
                    "CommandFlush.SetPosition",
                    warmup: 100,
                    iterations: 10_000,
                    () => FlushSetPosition(host, authority, tickets, proxyGenerations, handle));
            }
            finally
            {
                Object.DestroyImmediate(hostGo);
            }
        }

        static unsafe void FlushSetPosition(
            LuauPropHost host,
            LuauAuthorityTable authority,
            LuauTicketProcessor tickets,
            System.Collections.Generic.Dictionary<uint, LuauScriptProxy.ProxyGenerationState> proxyGenerations,
            LuauObjectHandle handle)
        {
            var cmd = new BasisLuauCommandNative
            {
                Type = (ushort)LuauCommandType.SetPosition,
                HostId = 1,
                HandleIndex = handle.Index,
                HandleGeneration = handle.Generation,
                ProxyId = 1,
                ProxyGeneration = 1,
            };
            cmd.Data[0] = 1f;
            cmd.Data[1] = 2f;
            cmd.Data[2] = 3f;
            LuauCommandFlush.Apply(host, authority, null, tickets, proxyGenerations, cmd);
        }
    }
}
