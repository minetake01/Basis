using System;
using Basis;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Network.Core;
using Luau;
using Minetake.Basis.Luau.Bindings;
using UnityEngine;

namespace Minetake.Basis.Luau.Services
{
    [LuauLibrary("basis_network")]
    public partial class BasisLuauNetworkBridge
    {
        public static void Install(LuauState state, LuauHostBase host)
        {
            state?.OpenLibrary<BasisLuauNetworkBridge>();
        }

        [LuauMember("makeNetworkable")]
        public static double MakeNetworkable(double handleRaw)
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

            if (!behaviour.TryGetComponent(out BasisNetworkShim shim))
            {
                shim = behaviour.gameObject.AddComponent<BasisNetworkShim>();
            }

            return host.RegisterObject(shim).ToRaw();
        }

        [LuauMember("sendBytes")]
        public static void SendBytes(double handleRaw, byte[] payload, int deliveryMethod)
        {
            var host = LuauBindingContext.Host;
            if (host == null || payload == null)
            {
                return;
            }

            var handle = Registry.LuauObjectHandle.FromRaw((ulong)handleRaw);
            if (!host.Registry.TryResolve(handle, out BasisNetworkShim shim))
            {
                return;
            }

            shim.SendCustomNetworkEvent(payload, (DeliveryMethod)deliveryMethod);
        }
    }

    public class BasisNetworkShim : BasisNetworkBehaviour
    {
        public event Action NetworkReady;
        public event Action ServerOwnershipDestroyed;
        public event Action<BasisNetworkPlayer> OwnershipTransfer;
        public event Action<ushort, byte[], DeliveryMethod> NetworkMessageReceived;
        public event Action<BasisNetworkPlayer> PlayerLeft;
        public event Action<BasisNetworkPlayer> PlayerJoined;

        public override void OnNetworkReady() => NetworkReady?.Invoke();
        public override void OnServerOwnershipDestroyed() => ServerOwnershipDestroyed?.Invoke();
        public override void OnOwnershipTransfer(BasisNetworkPlayer netNewOwner) => OwnershipTransfer?.Invoke(netNewOwner);
        public override void OnNetworkMessage(ushort playerId, byte[] buffer, DeliveryMethod deliveryMethod) =>
            NetworkMessageReceived?.Invoke(playerId, buffer, deliveryMethod);
        public override void OnPlayerLeft(BasisNetworkPlayer player) => PlayerLeft?.Invoke(player);
        public override void OnPlayerJoined(BasisNetworkPlayer player) => PlayerJoined?.Invoke(player);

        public void RequestOwnershipIfNone() => RequestWhoIsOwnershipAsync();
    }
}
