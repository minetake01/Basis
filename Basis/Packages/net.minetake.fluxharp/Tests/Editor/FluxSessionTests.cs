using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Net.Minetake.FluxHarp.Tests
{
    /// <summary>
    /// Everyone edits one expression at once. Whatever order requests arrive in, and whenever a peer joins or
    /// the owner leaves, every copy must end up the same graph.
    /// </summary>
    public class FluxSessionTests
    {
        /// <summary>An in-memory room: reliable messages queue per peer and are delivered on <see cref="Pump"/>.</summary>
        private sealed class Room
        {
            public readonly Dictionary<ushort, FluxSession> Peers = new Dictionary<ushort, FluxSession>();
            private readonly Queue<(ushort from, ushort to, byte[] packet)> _wire = new Queue<(ushort, ushort, byte[])>();

            public FluxSession Join(ushort id)
            {
                FluxSession s = new FluxSession((packet, reliable, recipient) =>
                {
                    foreach (ushort peer in Peers.Keys.ToArray())
                    {
                        if (peer == id) continue;
                        if (recipient.HasValue && recipient.Value != peer) continue;
                        _wire.Enqueue((id, peer, packet));
                    }
                });
                Peers.Add(id, s);
                return s;
            }

            public void Leave(ushort id) => Peers.Remove(id);

            public void Pump()
            {
                while (_wire.Count > 0)
                {
                    (ushort from, ushort to, byte[] packet) = _wire.Dequeue();
                    if (Peers.TryGetValue(to, out FluxSession s)) s.Receive(from, packet, 0);
                }
            }

            public void AssertConverged()
            {
                byte[] first = null;
                foreach (FluxSession s in Peers.Values)
                {
                    Assert.That(s.Synced, Is.True);
                    byte[] bytes = FluxCodec.Snapshot(s.Seq, s.Graph);
                    if (first == null) first = bytes;
                    else Assert.That(bytes, Is.EqualTo(first));
                }
            }
        }

        [Test]
        public void LateJoinerAndConcurrentEditsConverge()
        {
            Room room = new Room();
            FluxSession a = room.Join(1);
            a.BecomeOwner(1, 0, FluxPreset.Seed);

            FluxSession b = room.Join(2);
            b.FollowOwner(1);
            // A commit races the joiner's snapshot request.
            a.Request(FluxOp.Add(FluxKind.Const, new FluxVec(0.1f, 0.1f, 0), 3), 0);
            room.Pump();
            room.AssertConverged();

            FluxSession c = room.Join(3);
            c.FollowOwner(1);
            // Two players edit at the same moment; the owner orders them.
            b.Request(FluxOp.Add(FluxKind.Voice, new FluxVec(0, 0.2f, 0)), 0);
            c.Request(FluxOp.Add(FluxKind.Voice, new FluxVec(0, -0.2f, 0)), 0);
            c.Request(FluxOp.SetDj(2), 0);
            room.Pump();
            room.AssertConverged();
            Assert.That(a.Graph.CountOutputs(FluxKind.Voice), Is.EqualTo(3));
        }

        [Test]
        public void OwnerHandoverKeepsTheRoomsPiece()
        {
            Room room = new Room();
            FluxSession a = room.Join(1);
            a.BecomeOwner(1, 0, FluxPreset.Yxeni);
            FluxSession b = room.Join(2);
            b.FollowOwner(1);
            room.Pump();
            uint revision = b.Graph.Revision;

            room.Leave(1);
            b.BecomeOwner(2, 0, FluxPreset.Seed);
            Assert.That(b.Graph.Revision, Is.EqualTo(revision), "the new owner must not restart the piece");

            FluxSession c = room.Join(3);
            c.FollowOwner(2);
            b.Request(FluxOp.SetTempo(140), 0);
            room.Pump();
            room.AssertConverged();
        }

        [Test]
        public void RejectedEditReachesOnlyTheRequesterAndChangesNothing()
        {
            Room room = new Room();
            FluxSession a = room.Join(1);
            a.BecomeOwner(1, 0, FluxPreset.Seed);
            FluxSession b = room.Join(2);
            b.FollowOwner(1);
            room.Pump();

            string heard = null;
            b.Rejected += why => heard = why;
            uint seq = a.Seq;
            b.Request(FluxOp.SetTempo(9999), 0);
            room.Pump();

            Assert.That(heard, Is.Not.Null);
            Assert.That(a.Seq, Is.EqualTo(seq));
            room.AssertConverged();
        }
    }
}
