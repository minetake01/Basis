using System;
using Minetake.Basis.Luau.Runtime;
using Minetake.Basis.Luau.Services;
using NUnit.Framework;

namespace Minetake.Basis.Luau.Tests
{
    public class LuauRuntimeSecurityTests
    {
        [Test]
        public void CommandCatalog_DenyByDefault()
        {
            Assert.IsFalse(LuauCommandCatalog.IsRegistered(LuauCommandType.Invalid));
            Assert.IsFalse(LuauCommandCatalog.IsRegistered((LuauCommandType)999));
            Assert.IsTrue(LuauCommandCatalog.IsRegistered(LuauCommandType.SetPosition));
        }

        [Test]
        public void FailurePolicy_Scopes()
        {
            Assert.AreEqual(LuauFailureScope.Proxy, LuauFailurePolicy.ScopeFor(LuauFailureReason.Timeout));
            Assert.AreEqual(LuauFailureScope.Host, LuauFailurePolicy.ScopeFor(LuauFailureReason.AllocFailure));
        }

        [Test]
        public void BytecodeGate_RejectsEmpty()
        {
            LuauBytecodeGate.GateResult result = LuauBytecodeGate.ValidateForLoad(System.Array.Empty<byte>());
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void BytecodeSigner_RoundTrip()
        {
            byte[] payload = { 1, 2, 3, 4 };
            byte[] signed = LuauBytecodeSigner.AttachSignature(payload);
            Assert.AreEqual(payload.Length + LuauBytecodeGate.SignatureBytes, signed.Length);
            Assert.IsTrue(LuauBytecodeSigner.Verify(payload, new ReadOnlySpan<byte>(signed, payload.Length, LuauBytecodeGate.SignatureBytes)));
        }

        [Test]
        public void ImageDownloader_BlocksPrivateIp()
        {
            Assert.IsFalse(BasisLuauImageDownloader.TryValidateUrl("http://127.0.0.1/test.png", out _));
            Assert.IsFalse(BasisLuauImageDownloader.TryValidateUrl("http://192.168.0.1/test.png", out _));
            Assert.IsTrue(BasisLuauImageDownloader.TryValidateUrl("https://example.com/a.png", out _));
        }
    }
}
