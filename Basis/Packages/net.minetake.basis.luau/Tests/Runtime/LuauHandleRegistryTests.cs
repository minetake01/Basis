using Minetake.Basis.Luau.Registry;
using NUnit.Framework;
using UnityEngine;

namespace Minetake.Basis.Luau.Tests
{
    public class LuauHandleRegistryTests
    {
        [Test]
        public void StaleGeneration_IsRejected()
        {
            var host = new GameObject("host").AddComponent<LuauPropHost>();
            LuauObjectRegistry registry = host.Registry;
            var handle = registry.Register(host.transform);
            registry.Release(handle);
            var stale = LuauObjectHandle.FromRaw(((ulong)handle.Generation << 32) | handle.Index);
            Assert.That(registry.TryResolve(stale, out _), Is.False);
        }

        [Test]
        public void CrossHostHandle_IsRejected()
        {
            var hostA = new GameObject("hostA").AddComponent<LuauPropHost>();
            var hostB = new GameObject("hostB").AddComponent<LuauPropHost>();
            var handle = hostA.Registry.Register(hostA.transform);
            Assert.That(hostB.Registry.TryResolve(handle, out _), Is.False);
            Object.DestroyImmediate(hostA.gameObject);
            Object.DestroyImmediate(hostB.gameObject);
        }
    }
}
