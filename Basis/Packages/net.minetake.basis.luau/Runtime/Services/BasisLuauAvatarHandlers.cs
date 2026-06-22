using Minetake.Basis.Luau.Registry;
using Minetake.Basis.Luau.Runtime;
using UnityEngine;

namespace Minetake.Basis.Luau.Services
{
    public static class BasisLuauAvatarHandlers
    {
        public static LuauObjectHandle ResolveAvatar(LuauHostBase host, LuauAuthorityTable authority, LuauObjectHandle handle)
        {
            if (host == null || !authority.TryResolve(handle, out UnityEngine.Object obj))
            {
                return LuauObjectHandle.FromRaw(LuauObjectHandle.InvalidRaw);
            }

            if (obj is not MonoBehaviour behaviour)
            {
                return LuauObjectHandle.FromRaw(LuauObjectHandle.InvalidRaw);
            }

            if (!behaviour.TryGetComponent(out BasisLuauAvatarBridge bridge))
            {
                bridge = behaviour.gameObject.AddComponent<BasisLuauAvatarBridge>();
            }

            return authority.Register(bridge);
        }
    }
}
