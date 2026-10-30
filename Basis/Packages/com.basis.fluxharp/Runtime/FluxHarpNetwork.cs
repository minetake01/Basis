using System;
using Basis.Network.Core;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Unity.Scripting.LifecycleManagement;

namespace Basis.FluxHarp
{
    /// <summary>
    /// Carries the shared expression over the Basis scene channel. The instrument is part of the client, not a
    /// world, so it has no scene object to hang an identity on: one well-known name resolves to the id every
    /// client agrees on, and ownership of that same name decides who sequences edits. The server hands it to
    /// the longest-connected peer when the owner leaves, so a room always has a sequencer.
    ///
    /// <para>Offline there is no room; the local session owns itself and the instrument plays alone.</para>
    /// </summary>
    [AutoStaticsCleanup]
    public static partial class FluxHarpNetwork
    {
        public const string Identifier = "Basis.FluxHarp";
        private const BasisDebug.LogTag LogTag = BasisDebug.LogTag.Networking;

        public static FluxSession Session { get; private set; }

        private static bool _initialized;
        private static bool _hasId;
        private static ushort _id;
        private static int _generation;
        private static int _resolving = -1;
        private static readonly ushort[] _one = new ushort[1];

        /// <summary>The shared-clock now. The server's clock when connected, this machine's otherwise.</summary>
        public static long NowTicks => BasisNetworkManagement.RemoteUtcTime().Ticks;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            Session = new FluxSession(Send);

            BasisNetworkPlayer.OnLocalPlayerJoined -= HandleLocalJoined;
            BasisNetworkPlayer.OnLocalPlayerJoined += HandleLocalJoined;
            BasisNetworkPlayer.OnLocalPlayerLeft -= HandleLocalLeft;
            BasisNetworkPlayer.OnLocalPlayerLeft += HandleLocalLeft;
            BasisNetworkPlayer.OnPlayerJoined -= HandlePlayerJoined;
            BasisNetworkPlayer.OnPlayerJoined += HandlePlayerJoined;
            BasisNetworkPlayer.OnOwnershipTransfer -= HandleOwnership;
            BasisNetworkPlayer.OnOwnershipTransfer += HandleOwnership;
            UnityEngine.Application.quitting -= Shutdown;
            UnityEngine.Application.quitting += Shutdown;

            if (BasisNetworkConnection.LocalPlayerIsConnected) HandleLocalJoined(null, null);
            else Session.BecomeOwner(null, NowTicks, FluxPreset.Yxeni);
        }

        public static void Shutdown()
        {
            BasisNetworkPlayer.OnLocalPlayerJoined -= HandleLocalJoined;
            BasisNetworkPlayer.OnLocalPlayerLeft -= HandleLocalLeft;
            BasisNetworkPlayer.OnPlayerJoined -= HandlePlayerJoined;
            BasisNetworkPlayer.OnOwnershipTransfer -= HandleOwnership;
            UnityEngine.Application.quitting -= Shutdown;
            Release();
            _initialized = false;
            Session = null;
        }

        public static void Request(FluxOp op) => Session?.Request(op, NowTicks);

        public static void Drag(ushort node, FluxVec position)
        {
            if (_hasId) Session?.Drag(node, position);
        }

        private static async void HandleLocalJoined(BasisNetworkPlayer player, BasisLocalPlayer local)
        {
            if (!BasisNetworkConnection.LocalPlayerIsConnected) return;
            int generation = _generation;
            if (_hasId || _resolving == generation) return;
            _resolving = generation;

            // The room's piece replaces whatever this client was playing alone.
            Session.Reset();

            BasisIdResolutionResult resolution = await BasisNetworkIdResolver.ResolveAsync(Identifier);
            if (!_initialized || _hasId || generation != _generation) return;
            if (!resolution.Success)
            {
                _resolving = -1;
                BasisDebug.LogError($"Flux harp could not resolve '{Identifier}'; the instrument stays local.", LogTag);
                Session.BecomeOwner(null, NowTicks, FluxPreset.Yxeni);
                return;
            }

            _id = resolution.Id;
            _hasId = true;
            BasisNetworkGenericMessages.RegisterHandler(_id, OnMessage);

            // The first to ask becomes the owner; everyone else is told who it is.
            BasisOwnershipResult owner = await BasisNetworkOwnership.RequestCurrentOwnershipAsync(Identifier);
            if (!_initialized || generation != _generation) return;
            if (!owner.Success)
            {
                BasisDebug.LogError("Flux harp could not learn who owns the session.", LogTag);
                return;
            }
            ApplyOwner(owner.PlayerId);
        }

        private static void HandleLocalLeft(BasisNetworkPlayer player, BasisLocalPlayer local)
        {
            Release();
            // Alone again: keep playing what the room was playing, now as its own owner.
            Session?.BecomeOwner(null, NowTicks, FluxPreset.Yxeni);
        }

        private static void Release()
        {
            _generation++;
            _resolving = -1;
            if (_hasId) BasisNetworkGenericMessages.UnregisterHandler(_id);
            _hasId = false;
            _id = 0;
        }

        private static void HandlePlayerJoined(BasisNetworkPlayer player)
        {
            if (player == null || !_hasId || player.IsLocal) return;
            Session.PeerJoined(player.playerId);
        }

        private static void HandleOwnership(string ownershipId, ushort playerId, bool isLocalOwner)
        {
            if (ownershipId != Identifier || Session == null) return;
            ApplyOwner(playerId);
        }

        private static void ApplyOwner(ushort ownerId)
        {
            if (!BasisNetworkConnection.TryGetLocalPlayerID(out ushort localId)) return;
            if (ownerId == localId) Session.BecomeOwner(localId, NowTicks, FluxPreset.Yxeni);
            else Session.FollowOwner(ownerId);
        }

        private static void OnMessage(ushort sender, byte[] buffer, DeliveryMethod delivery)
        {
            Session?.Receive(sender, buffer, NowTicks);
        }

        private static void Send(byte[] packet, bool reliable, ushort? recipient)
        {
            if (!_hasId) return;
            ushort[] recipients = null;
            if (recipient.HasValue)
            {
                _one[0] = recipient.Value;
                recipients = _one;
            }
            BasisNetworkGenericMessages.OnNetworkMessageSend(_id, packet,
                reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable, recipients);
        }
    }
}
