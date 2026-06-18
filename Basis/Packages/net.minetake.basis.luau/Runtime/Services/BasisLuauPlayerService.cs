using Basis.Scripts.BasisSdk.Players;
using Luau;
using Minetake.Basis.Luau.Bindings;
using UnityEngine;

namespace Minetake.Basis.Luau.Services
{
    [LuauLibrary("basis_player")]
    public partial class BasisLuauPlayerService
    {
        public static void Install(LuauState state, LuauHostBase host)
        {
            state?.OpenLibrary<BasisLuauPlayerService>();
        }

        [LuauMember("getInstance")]
        public static double GetInstance()
        {
            var host = LuauBindingContext.Host;
            if (host == null || BasisLocalPlayer.Instance == null)
            {
                return 0;
            }

            return host.RegisterObject(BasisLocalPlayer.Instance).ToRaw();
        }

        [LuauMember("getPositionAndRotation")]
        public static void GetPositionAndRotation(double handleRaw)
        {
            if (!TryGetPlayer(handleRaw, out var player))
            {
                return;
            }

            player.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
            // Results consumed via basis_object on returned handles in future; store on host table if needed.
        }

        [LuauMember("teleport")]
        public static void Teleport(double handleRaw, double x, double y, double z)
        {
            if (TryGetPlayer(handleRaw, out var player))
            {
                player.Teleport(new Vector3((float)x, (float)y, (float)z), Quaternion.identity);
            }
        }

        [LuauMember("respawn")]
        public static void Respawn(double handleRaw)
        {
            if (TryGetPlayer(handleRaw, out var player))
            {
                player.Respawn();
            }
        }

        static bool TryGetPlayer(double handleRaw, out BasisLocalPlayer player)
        {
            player = null;
            var host = LuauBindingContext.Host;
            if (host == null)
            {
                return false;
            }

            if (handleRaw == 0)
            {
                player = BasisLocalPlayer.Instance;
                return player != null;
            }

            var handle = Registry.LuauObjectHandle.FromRaw((ulong)handleRaw);
            if (!host.Registry.TryResolve(handle, out UnityEngine.Object obj))
            {
                return false;
            }

            player = obj as BasisLocalPlayer;
            return player != null;
        }
    }
}
