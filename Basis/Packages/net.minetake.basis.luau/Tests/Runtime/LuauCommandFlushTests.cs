using Luau.Unity;
using Minetake.Basis.Luau.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Minetake.Basis.Luau.Tests
{
    public class LuauCommandFlushTests
    {
        [Test]
        public void RejectsUnregisteredCommand()
        {
            var hostGo = new GameObject("host");
            var host = hostGo.AddComponent<LuauPropHost>();
            var authority = new LuauAuthorityTable(1, hostGo.transform, LuauHostKind.Prop);
            var tickets = new LuauTicketProcessor(host, authority, new LuauTicketRegistry());
            var proxyGenerations = new System.Collections.Generic.Dictionary<uint, LuauScriptProxy.ProxyGenerationState>();

            var cmd = new BasisLuauCommandNative { Type = 999, HostId = 1 };
            LuauCommandFlush.Apply(host, authority, null, tickets, proxyGenerations, cmd);
            Object.DestroyImmediate(hostGo);
        }

        [Test]
        public void RejectsStaleProxyGeneration()
        {
            var hostGo = new GameObject("host");
            var host = hostGo.AddComponent<LuauPropHost>();
            var authority = new LuauAuthorityTable(1, hostGo.transform, LuauHostKind.Prop);
            var tickets = new LuauTicketProcessor(host, authority, new LuauTicketRegistry());
            var proxyGenerations = new System.Collections.Generic.Dictionary<uint, LuauScriptProxy.ProxyGenerationState>
            {
                [7] = new LuauScriptProxy.ProxyGenerationState { Generation = 2, Disabled = false },
            };

            var cmd = new BasisLuauCommandNative
            {
                Type = (ushort)LuauCommandType.SetPosition,
                HostId = 1,
                ProxyId = 7,
                ProxyGeneration = 1,
            };

            LuauCommandFlush.Apply(host, authority, null, tickets, proxyGenerations, cmd);
            Object.DestroyImmediate(hostGo);
        }
    }
}
