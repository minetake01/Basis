using Luau;
using Minetake.Basis.Luau.Bindings;
using UnityEngine;

namespace Minetake.Basis.Luau.Services
{
    [LuauLibrary("basis_avatar")]
    public partial class BasisLuauAvatarService
    {
        public static void Install(LuauState state, LuauHostBase host)
        {
            state?.OpenLibrary<BasisLuauAvatarService>();
        }

        [LuauMember("resolveAvatar")]
        public static double ResolveAvatar(double handleRaw)
        {
            var host = LuauBindingContext.Host;
            if (host == null)
            {
                return 0;
            }

            var handle = Registry.LuauObjectHandle.FromRaw((ulong)handleRaw);
            if (!host.Registry.TryResolve(handle, out UnityEngine.Object obj))
            {
                return 0;
            }

            if (obj is not MonoBehaviour behaviour)
            {
                return 0;
            }

            if (!behaviour.TryGetComponent(out BasisLuauAvatarBridge bridge))
            {
                bridge = behaviour.gameObject.AddComponent<BasisLuauAvatarBridge>();
            }

            return host.RegisterObject(bridge).ToRaw();
        }

        [LuauMember("isReady")]
        public static bool IsReady(double handleRaw)
        {
            return TryGetBridge(handleRaw, out var bridge) && bridge.IsReady;
        }

        [LuauMember("isLocalPlayer")]
        public static bool IsLocalPlayer(double handleRaw) =>
            TryGetBridge(handleRaw, out var bridge) && bridge.IsLocalPlayer;

        [LuauMember("setHumanScale")]
        public static void SetHumanScale(double handleRaw, double scale)
        {
            if (TryGetBridge(handleRaw, out var bridge))
            {
                bridge.HumanScale = (float)scale;
            }
        }

        [LuauMember("onReady")]
        public static void OnReady(double handleRaw, LuauFunction callback)
        {
            if (!TryGetBridge(handleRaw, out var bridge) || callback == null)
            {
                return;
            }

            var host = LuauBindingContext.Host;
            var proxy = LuauBindingContext.Proxy;
            if (host == null || proxy == null)
            {
                return;
            }

            bridge.OnAvatarReady = isOwner =>
            {
                host.InvokeCallback(proxy, callback, isOwner);
            };
        }

        static bool TryGetBridge(double handleRaw, out BasisLuauAvatarBridge bridge)
        {
            bridge = null;
            var host = LuauBindingContext.Host;
            if (host == null)
            {
                return false;
            }

            var handle = Registry.LuauObjectHandle.FromRaw((ulong)handleRaw);
            if (!host.Registry.TryResolve(handle, out UnityEngine.Object obj))
            {
                return false;
            }

            bridge = obj as BasisLuauAvatarBridge;
            return bridge != null;
        }
    }
}
