using System;
using System.Collections.Generic;
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
        public static double MakeNetworkable(double handleRaw) =>
            BasisLuauSafeUtil.MakeNetworkable(ResolveBehaviour(handleRaw)) is { } shim
                ? LuauBindingContext.Host.RegisterObject(shim).ToRaw()
                : 0;

        [LuauMember("sendBytes")]
        public static void SendBytes(double handleRaw, byte[] payload, int deliveryMethod)
        {
            if (TryGetShim(handleRaw, out var shim) && payload != null)
            {
                shim.SendCustomNetworkEvent(payload, (DeliveryMethod)deliveryMethod);
            }
        }

        [LuauMember("sendToPlayers")]
        public static void SendToPlayers(double handleRaw, byte[] payload, int deliveryMethod, double[] playerIds)
        {
            if (!TryGetShim(handleRaw, out var shim) || payload == null || playerIds == null)
            {
                return;
            }

            var ids = new ushort[playerIds.Length];
            for (int i = 0; i < playerIds.Length; i++)
            {
                ids[i] = (ushort)playerIds[i];
            }

            shim.SendCustomNetworkEvent(payload, (DeliveryMethod)deliveryMethod, ids);
        }

        [LuauMember("takeOwnership")]
        public static void TakeOwnership(double handleRaw)
        {
            if (TryGetShim(handleRaw, out var shim))
            {
                shim.TakeOwnership();
            }
        }

        [LuauMember("requestOwnershipIfNone")]
        public static void RequestOwnershipIfNone(double handleRaw)
        {
            if (TryGetShim(handleRaw, out var shim))
            {
                shim.RequestOwnershipIfNone();
            }
        }

        [LuauMember("isLocalOwner")]
        public static bool IsLocalOwner(double handleRaw) =>
            TryGetShim(handleRaw, out var shim) && shim.IsLocalOwner();

        [LuauMember("onNetworkReady")]
        public static void OnNetworkReady(double handleRaw, LuauFunction callback) =>
            BindEvent(handleRaw, callback, (shim, fn) => shim.NetworkReady += () => Fire(fn));

        [LuauMember("onMessage")]
        public static void OnMessage(double handleRaw, LuauFunction callback) =>
            BindEvent(handleRaw, callback, (shim, fn) =>             shim.NetworkMessageReceived += (id, buffer, method) =>
            {
                var host = LuauBindingContext.Host;
                var proxy = LuauBindingContext.Proxy;
                if (host == null || proxy == null)
                {
                    return;
                }

                host.InvokeCallback(proxy, fn, id, (int)method, buffer?.Length ?? 0);
            });

        [LuauMember("onPlayerJoined")]
        public static void OnPlayerJoined(double handleRaw, LuauFunction callback) =>
            BindEvent(handleRaw, callback, (shim, fn) => shim.PlayerJoined += player => FireWithPlayer(fn, player));

        [LuauMember("onPlayerLeft")]
        public static void OnPlayerLeft(double handleRaw, LuauFunction callback) =>
            BindEvent(handleRaw, callback, (shim, fn) => shim.PlayerLeft += player => FireWithPlayer(fn, player));

        [LuauMember("onOwnershipTransfer")]
        public static void OnOwnershipTransfer(double handleRaw, LuauFunction callback) =>
            BindEvent(handleRaw, callback, (shim, fn) => shim.OwnershipTransfer += player => FireWithPlayer(fn, player));

        static void BindEvent(double handleRaw, LuauFunction callback, Action<BasisNetworkShim, LuauFunction> bind)
        {
            if (!TryGetShim(handleRaw, out var shim) || callback == null)
            {
                return;
            }

            bind(shim, callback);
        }

        static void Fire(LuauFunction fn)
        {
            var host = LuauBindingContext.Host;
            var proxy = LuauBindingContext.Proxy;
            host?.InvokeCallback(proxy, fn);
        }

        static void FireWithPlayer(LuauFunction fn, BasisNetworkPlayer player)
        {
            var host = LuauBindingContext.Host;
            var proxy = LuauBindingContext.Proxy;
            if (host == null || proxy == null || player == null)
            {
                return;
            }

            host.InvokeCallback(proxy, fn, player.playerId);
        }

        static MonoBehaviour ResolveBehaviour(double handleRaw)
        {
            var host = LuauBindingContext.Host;
            if (host == null)
            {
                return null;
            }

            var handle = Registry.LuauObjectHandle.FromRaw((ulong)handleRaw);
            return host.Registry.TryResolve(handle, out UnityEngine.Object obj) ? obj as MonoBehaviour : null;
        }

        static bool TryGetShim(double handleRaw, out BasisNetworkShim shim)
        {
            shim = null;
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

            shim = obj as BasisNetworkShim;
            return shim != null;
        }
    }

    public class BasisNetworkShim : BasisNetworkBehaviour
    {
        public delegate void NetworkReadyEvent();
        public delegate void ServerOwnershipDestroyedEvent();
        public delegate void OwnershipTransferEvent(BasisNetworkPlayer player);
        public delegate void NetworkMessageEvent(ushort playerId, byte[] buffer, DeliveryMethod deliveryMethod);
        public delegate void PlayerJoinedEvent(BasisNetworkPlayer player);
        public delegate void PlayerLeftEvent(BasisNetworkPlayer player);

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
