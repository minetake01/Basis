using System;
using System.Collections.Generic;

namespace Basis.FluxHarp
{
    /// <summary>
    /// The replicated expression, independent of any transport. One peer is the owner: it validates and
    /// sequences every edit. Everyone, owner included, changes the graph only by applying commits in sequence,
    /// so every copy goes through the same states. A joiner holds commits until a snapshot tells it where the
    /// sequence stands.
    /// </summary>
    public sealed class FluxSession
    {
        /// <summary>Sends a packet. <c>recipient</c> null means everyone else.</summary>
        public delegate void SendFn(byte[] packet, bool reliable, ushort? recipient);

        private readonly SendFn _send;
        private readonly List<(uint seq, FluxOp op)> _held = new List<(uint, FluxOp)>();

        public FluxGraph Graph { get; private set; } = new FluxGraph();
        public uint Seq { get; private set; }
        public bool IsOwner { get; private set; }
        public ushort? OwnerId { get; private set; }

        /// <summary>A joiner is not synced until its first snapshot; until then its graph is not the room's.</summary>
        public bool Synced { get; private set; }

        /// <summary>Raised after every applied commit or snapshot.</summary>
        public event Action<FluxOp?> Changed;

        /// <summary>Raised when somebody else is dragging a node (transient, not part of the graph).</summary>
        public event Action<ushort, FluxVec> RemoteDrag;

        /// <summary>Raised when the owner turns an edit down, so the requester can show it.</summary>
        public event Action<string> Rejected;

        public FluxSession(SendFn send)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        /// <summary>
        /// Becomes the owner. With nothing synced (a fresh room, or offline) the owner starts the piece itself;
        /// a synced peer taking over keeps the room's graph and sequence.
        /// </summary>
        public void BecomeOwner(ushort? localId, long nowTicks, FluxPreset opening)
        {
            IsOwner = true;
            OwnerId = localId;
            if (!Synced)
            {
                Graph = new FluxGraph();
                Seq = 0;
                Synced = true;
                _held.Clear();
                FluxOp start = FluxOp.LoadPreset(opening);
                Commit(ref start, nowTicks);
            }
            Changed?.Invoke(null);
        }

        /// <summary>Somebody else owns the session. Until their snapshot lands this copy is not synced.</summary>
        public void FollowOwner(ushort ownerId)
        {
            bool wasOwner = IsOwner;
            IsOwner = false;
            OwnerId = ownerId;
            if (wasOwner) return;
            if (!Synced) _send(new[] { (byte)FluxPacket.Hello }, true, ownerId);
        }

        /// <summary>Back to nothing: the connection is gone.</summary>
        public void Reset()
        {
            Graph = new FluxGraph();
            Seq = 0;
            IsOwner = false;
            OwnerId = null;
            Synced = false;
            _held.Clear();
            Changed?.Invoke(null);
        }

        /// <summary>The local player asks for an edit.</summary>
        public void Request(FluxOp op, long nowTicks)
        {
            if (IsOwner)
            {
                Commit(ref op, nowTicks);
                return;
            }
            if (OwnerId == null) return;
            _send(FluxCodec.Request(op), true, OwnerId);
        }

        /// <summary>The local player is dragging a node; everyone else sees it move before the edit lands.</summary>
        public void Drag(ushort node, FluxVec position) => _send(FluxCodec.Drag(node, position), false, null);

        /// <summary>A new peer arrived; the owner tells it where the room stands.</summary>
        public void PeerJoined(ushort peer)
        {
            if (IsOwner && Synced) _send(FluxCodec.Snapshot(Seq, Graph), true, peer);
        }

        public void Receive(ushort sender, byte[] packet, long nowTicks)
        {
            if (!FluxCodec.TryReadType(packet, out FluxPacket type)) return;
            switch (type)
            {
                case FluxPacket.Request:
                    if (IsOwner && FluxCodec.TryReadRequest(packet, out FluxOp request)) Commit(ref request, nowTicks, sender);
                    break;

                case FluxPacket.Commit:
                    if (!IsOwner && FluxCodec.TryReadCommit(packet, out uint seq, out FluxOp op)) OnCommit(seq, op);
                    break;

                case FluxPacket.Snapshot:
                    if (!IsOwner && FluxCodec.TryReadSnapshot(packet, out uint snapSeq, out FluxGraph graph)) OnSnapshot(snapSeq, graph);
                    break;

                case FluxPacket.Drag:
                    if (FluxCodec.TryReadDrag(packet, out ushort node, out FluxVec position)) RemoteDrag?.Invoke(node, position);
                    break;

                case FluxPacket.Hello:
                    PeerJoined(sender);
                    break;

                case FluxPacket.Reject:
                    Rejected?.Invoke(FluxCodec.ReadReject(packet));
                    break;
            }
        }

        private void Commit(ref FluxOp op, long nowTicks, ushort? requester = null)
        {
            string why = Graph.Validate(ref op, nowTicks);
            if (why != null)
            {
                if (requester.HasValue) _send(FluxCodec.Reject(why), true, requester);
                else Rejected?.Invoke(why);
                return;
            }
            Graph.Apply(op);
            Seq++;
            _send(FluxCodec.Commit(Seq, op), true, null);
            Changed?.Invoke(op);
        }

        private void OnCommit(uint seq, in FluxOp op)
        {
            if (!Synced)
            {
                _held.Add((seq, op));
                return;
            }
            if (seq <= Seq) return;
            if (seq != Seq + 1)
            {
                // Reliable-ordered delivery cannot skip; a gap means this copy is no longer the room's. Ask again.
                Synced = false;
                _held.Clear();
                _held.Add((seq, op));
                if (OwnerId.HasValue) _send(new[] { (byte)FluxPacket.Hello }, true, OwnerId);
                return;
            }
            Graph.Apply(op);
            Seq = seq;
            Changed?.Invoke(op);
        }

        private void OnSnapshot(uint seq, FluxGraph graph)
        {
            if (Synced && seq <= Seq) return;
            Graph = graph;
            Seq = seq;
            Synced = true;
            _held.Sort((a, b) => a.seq.CompareTo(b.seq));
            foreach ((uint heldSeq, FluxOp op) in _held)
            {
                if (heldSeq != Seq + 1) continue;
                Graph.Apply(op);
                Seq = heldSeq;
            }
            _held.Clear();
            Changed?.Invoke(null);
        }
    }
}
