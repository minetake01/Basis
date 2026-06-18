using System;

namespace Minetake.Basis.Luau.Policy
{
    /// <summary>
    /// Cilbox <c>CilboxPublicUtils</c> equivalent retained for whitelist parity.
    /// </summary>
    public static class LuauPublicUtils
    {
        public static void InitializeArray(Array array, byte[] data)
        {
            if (array == null || data == null)
            {
                return;
            }

            Buffer.BlockCopy(data, 0, array, 0, Math.Min(data.Length, Buffer.ByteLength(array)));
        }
    }
}
