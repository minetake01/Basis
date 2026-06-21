using System;
using System.Security.Cryptography;
using System.Text;
using Luau.Unity;
using Minetake.Basis.Luau.Runtime;
using UnityEngine;

namespace Minetake.Basis.Luau.Runtime
{
    public static class LuauBytecodeGate
    {
        public const int SignatureBytes = 32;

        public readonly struct GateResult
        {
            public bool Success { get; init; }
            public LuauFailureReason Reason { get; init; }
            public string Message { get; init; }
            public ReadOnlyMemory<byte> VerifiedBytecode { get; init; }
        }

        public static GateResult ValidateForLoad(ReadOnlySpan<byte> payload)
        {
            var settings = BasisLuauRuntimeSettings.GetOrCreate();

            if (payload.IsEmpty)
            {
                return Fail(LuauFailureReason.BytecodeRejected, "bytecode empty");
            }

            if (payload.Length > settings.maxBytecodeBytes)
            {
                return Fail(LuauFailureReason.BytecodeRejected, "bytecode exceeds max size");
            }

            ReadOnlySpan<byte> bytecode;
            ReadOnlySpan<byte> signature = ReadOnlySpan<byte>.Empty;

            if (payload.Length > SignatureBytes)
            {
                int bytecodeLen = payload.Length - SignatureBytes;
                bytecode = payload.Slice(0, bytecodeLen);
                signature = payload.Slice(bytecodeLen);
            }
            else
            {
                bytecode = payload;
            }

            if (BasisLuauNativeRuntime.IsAvailable)
            {
                unsafe
                {
                    fixed (byte* ptr = bytecode)
                    {
                        BasisLuauVerifyError err = BasisLuauNativeRuntime.VerifyBytecode(
                            ptr,
                            (nuint)bytecode.Length,
                            (nuint)settings.maxBytecodeBytes);

                        if (err != BasisLuauVerifyError.Ok)
                        {
                            return Fail(LuauFailureReason.BytecodeRejected, $"verifier: {err}");
                        }
                    }
                }
            }

            if (signature.Length == SignatureBytes)
            {
                if (!LuauBytecodeSigner.Verify(bytecode, signature))
                {
                    return Fail(LuauFailureReason.SignatureRejected, "signature mismatch");
                }
            }
            else if (!settings.MayLoadUnsignedBytecode() && settings.requireSignedBytecode)
            {
                return Fail(LuauFailureReason.SignatureRejected, "unsigned bytecode rejected");
            }

            return new GateResult
            {
                Success = true,
                VerifiedBytecode = bytecode.ToArray(),
            };
        }

        static GateResult Fail(LuauFailureReason reason, string message) => new()
        {
            Success = false,
            Reason = reason,
            Message = message,
        };
    }

    public static class LuauBytecodeSigner
    {
        static readonly byte[] DevKey = Encoding.UTF8.GetBytes("BasisLuauDevSigningKey-v1-ChangeInProduction!");

        public static byte[] Sign(ReadOnlySpan<byte> bytecode)
        {
            using var hmac = new HMACSHA256(DevKey);
            return hmac.ComputeHash(bytecode.ToArray());
        }

        public static byte[] AttachSignature(ReadOnlySpan<byte> bytecode)
        {
            byte[] sig = Sign(bytecode);
            var combined = new byte[bytecode.Length + sig.Length];
            bytecode.CopyTo(combined);
            sig.CopyTo(combined.AsSpan(bytecode.Length));
            return combined;
        }

        public static bool Verify(ReadOnlySpan<byte> bytecode, ReadOnlySpan<byte> signature)
        {
            if (signature.Length != LuauBytecodeGate.SignatureBytes)
            {
                return false;
            }

            byte[] expected = Sign(bytecode);
            return CryptographicOperations.FixedTimeEquals(expected, signature);
        }
    }
}
