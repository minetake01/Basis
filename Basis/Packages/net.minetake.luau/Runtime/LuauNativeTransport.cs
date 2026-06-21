using System;
using Luau.Unity;

namespace Luau.Unity
{
    public static unsafe class LuauNativeTransport
    {
        public static BasisLuauVerifyError VerifyBytecode(ReadOnlySpan<byte> bytecode, uint maxBytes)
        {
            if (!BasisLuauNativeRuntime.IsAvailable)
            {
                return BasisLuauVerifyError.Ok;
            }

            fixed (byte* ptr = bytecode)
            {
                return BasisLuauNativeRuntime.VerifyBytecode(ptr, (nuint)bytecode.Length, (nuint)maxBytes);
            }
        }
    }
}
