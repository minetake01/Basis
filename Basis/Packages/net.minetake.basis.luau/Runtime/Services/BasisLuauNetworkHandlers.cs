using Minetake.Basis.Luau.Registry;
using Minetake.Basis.Luau.Runtime;
using UnityEngine;

namespace Minetake.Basis.Luau.Services
{
    public static class BasisLuauNetworkHandlers
    {
        public static LuauObjectHandle MakeNetworkable(LuauHostBase host, LuauAuthorityTable authority, LuauObjectHandle handle)
        {
            if (host == null || !authority.TryResolve(handle, out UnityEngine.Object obj))
            {
                return LuauObjectHandle.FromRaw(LuauObjectHandle.InvalidRaw);
            }

            BasisNetworkShim shim = BasisLuauSafeUtil.MakeNetworkable(obj as MonoBehaviour);
            return shim != null ? authority.Register(shim) : LuauObjectHandle.FromRaw(LuauObjectHandle.InvalidRaw);
        }

        public static void TakeOwnership(LuauHostBase host, LuauAuthorityTable authority, LuauObjectHandle handle)
        {
            if (!authority.TryResolve(handle, out UnityEngine.Object obj))
            {
                return;
            }

            if (obj is BasisNetworkShim shim)
            {
                shim.TakeOwnership();
            }
        }
    }
}
