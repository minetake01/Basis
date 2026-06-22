using Minetake.Basis.Luau.Registry;
using Minetake.Basis.Luau.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Minetake.Basis.Luau.Tests
{
    public sealed class LuauAuthorityTableTests
    {
        [Test]
        public void StaleGenerationRejected()
        {
            var hostGo = new GameObject("host");
            var authority = new LuauAuthorityTable(1, hostGo.transform, LuauHostKind.Prop);
            var target = new GameObject("target");
            LuauObjectHandle handle = authority.Register(target);
            authority.Tombstone(handle.Index, handle.Generation);
            Assert.IsFalse(authority.TryResolve(handle, out _));
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(hostGo);
        }

        [Test]
        public void ContentRootRejectsOutsideTransform()
        {
            var hostGo = new GameObject("host");
            var authority = new LuauAuthorityTable(1, hostGo.transform, LuauHostKind.Prop);
            var outside = new GameObject("outside");
            LuauObjectHandle handle = authority.Register(outside.transform);
            Assert.IsFalse(authority.TryValidate(handle.Index, handle.Generation, typeof(Transform), out _));
            Object.DestroyImmediate(outside);
            Object.DestroyImmediate(hostGo);
        }
    }
}
