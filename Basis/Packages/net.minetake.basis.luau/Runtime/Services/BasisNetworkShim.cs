using Basis;
using Basis.Network.Core;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using UnityEngine;

namespace Minetake.Basis.Luau.Services
{
    public class BasisNetworkShim : BasisNetworkBehaviour
    {
        public event System.Action NetworkReady;
        public event System.Action ServerOwnershipDestroyed;
        public event System.Action<BasisNetworkPlayer> OwnershipTransfer;
        public event System.Action<ushort, byte[], DeliveryMethod> NetworkMessageReceived;
        public event System.Action<BasisNetworkPlayer> PlayerLeft;
        public event System.Action<BasisNetworkPlayer> PlayerJoined;

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
