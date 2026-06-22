using Minetake.Basis.Luau.Registry;
using Minetake.Basis.Luau.Runtime;
using UnityEngine;

namespace Minetake.Basis.Luau.Services
{
    public static class BasisLuauUtilHandlers
    {
        public static LuauObjectHandle MakeInteractable(LuauHostBase host, LuauAuthorityTable authority, LuauObjectHandle handle)
        {
            if (host == null || !authority.TryResolve(handle, out UnityEngine.Object obj))
            {
                return LuauObjectHandle.FromRaw(LuauObjectHandle.InvalidRaw);
            }

            BasisLuauInteractableShim shim = BasisLuauSafeUtil.MakeInteractable(obj);
            return shim != null ? authority.Register(shim) : LuauObjectHandle.FromRaw(LuauObjectHandle.InvalidRaw);
        }
    }
}
