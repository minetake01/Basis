using Basis.Scripts.BasisSdk;
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
    }

    public sealed class BasisLuauAvatarBridge : MonoBehaviour
    {
        BasisAvatar _avatar;

        public bool IsReady => _avatar != null && _avatar.IsReady;
        public bool IsOwnedLocally => _avatar != null && _avatar.IsOwnedLocally;
        public Animator Animator => _avatar != null ? _avatar.Animator : null;

        void Awake()
        {
            _avatar = GetComponent<BasisAvatar>() ?? GetComponentInParent<BasisAvatar>(true);
        }
    }
}
