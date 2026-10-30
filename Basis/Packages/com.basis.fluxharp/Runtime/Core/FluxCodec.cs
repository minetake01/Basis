using System;
using System.IO;
using System.Text;

namespace Basis.FluxHarp
{
    /// <summary>The leading byte of every flux packet.</summary>
    public enum FluxPacket : byte
    {
        /// <summary>Anyone → owner: please apply this edit.</summary>
        Request = 1,

        /// <summary>Owner → everyone: this edit is committed as number <c>seq</c>.</summary>
        Commit = 2,

        /// <summary>Owner → one or all: the whole expression as of commit <c>seq</c>.</summary>
        Snapshot = 3,

        /// <summary>Anyone → everyone: a node is being dragged (transient, unreliable).</summary>
        Drag = 4,

        /// <summary>Joiner → owner: I have nothing, send me the room.</summary>
        Hello = 5,

        /// <summary>Owner → requester: your edit was turned down, and why.</summary>
        Reject = 6,
    }

    /// <summary>
    /// Little-endian binary forms of ops and snapshots. Readers reject anything malformed rather than
    /// guessing, because a half-read edit would leave this copy of the expression different from the others.
    /// </summary>
    public static class FluxCodec
    {
        public const int OpSize = 1 + 2 + 1 + 1 + 2 + 8 + 12 + 8 + 8;

        public static byte[] Request(in FluxOp op)
        {
            using MemoryStream ms = new MemoryStream(1 + OpSize);
            using BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write((byte)FluxPacket.Request);
            WriteOp(w, op);
            return ms.ToArray();
        }

        public static byte[] Commit(uint seq, in FluxOp op)
        {
            using MemoryStream ms = new MemoryStream(1 + 4 + OpSize);
            using BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write((byte)FluxPacket.Commit);
            w.Write(seq);
            WriteOp(w, op);
            return ms.ToArray();
        }

        public static byte[] Drag(ushort node, FluxVec position)
        {
            using MemoryStream ms = new MemoryStream(1 + 2 + 12);
            using BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write((byte)FluxPacket.Drag);
            w.Write(node);
            WriteVec(w, position);
            return ms.ToArray();
        }

        public static byte[] Snapshot(uint seq, FluxGraph graph)
        {
            using MemoryStream ms = new MemoryStream(64 + graph.Count * 40);
            using BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write((byte)FluxPacket.Snapshot);
            w.Write(seq);
            WriteGraph(w, graph);
            return ms.ToArray();
        }

        public static bool TryReadType(byte[] buffer, out FluxPacket type)
        {
            type = default;
            if (buffer == null || buffer.Length < 1) return false;
            type = (FluxPacket)buffer[0];
            return type >= FluxPacket.Request && type <= FluxPacket.Reject;
        }

        public static byte[] Reject(string why)
        {
            byte[] text = Encoding.UTF8.GetBytes(why.Length > 64 ? why.Substring(0, 64) : why);
            byte[] packet = new byte[1 + text.Length];
            packet[0] = (byte)FluxPacket.Reject;
            Array.Copy(text, 0, packet, 1, text.Length);
            return packet;
        }

        public static string ReadReject(byte[] packet) => Encoding.UTF8.GetString(packet, 1, packet.Length - 1);

        public static bool TryReadRequest(byte[] buffer, out FluxOp op)
        {
            op = default;
            if (buffer == null || buffer.Length != 1 + OpSize || buffer[0] != (byte)FluxPacket.Request) return false;
            using BinaryReader r = Reader(buffer);
            return TryReadOp(r, out op);
        }

        public static bool TryReadCommit(byte[] buffer, out uint seq, out FluxOp op)
        {
            seq = 0;
            op = default;
            if (buffer == null || buffer.Length != 1 + 4 + OpSize || buffer[0] != (byte)FluxPacket.Commit) return false;
            using BinaryReader r = Reader(buffer);
            seq = r.ReadUInt32();
            return TryReadOp(r, out op);
        }

        public static bool TryReadDrag(byte[] buffer, out ushort node, out FluxVec position)
        {
            node = 0;
            position = default;
            if (buffer == null || buffer.Length != 1 + 2 + 12 || buffer[0] != (byte)FluxPacket.Drag) return false;
            using BinaryReader r = Reader(buffer);
            node = r.ReadUInt16();
            position = ReadVec(r);
            return position.IsFinite;
        }

        public static bool TryReadSnapshot(byte[] buffer, out uint seq, out FluxGraph graph)
        {
            seq = 0;
            graph = null;
            if (buffer == null || buffer.Length < 5 || buffer[0] != (byte)FluxPacket.Snapshot) return false;
            try
            {
                using BinaryReader r = Reader(buffer);
                seq = r.ReadUInt32();
                graph = ReadGraph(r);
                return r.BaseStream.Position == buffer.Length;
            }
            catch (Exception e) when (e is EndOfStreamException || e is InvalidDataException)
            {
                graph = null;
                return false;
            }
        }

        public static void WriteGraph(BinaryWriter w, FluxGraph graph)
        {
            w.Write(graph.Transport.EpochTicks);
            w.Write(graph.Transport.Bpm);
            w.Write(graph.Transport.Dj);
            w.Write(graph.Transport.NextDj);
            w.Write(graph.Transport.SwitchBeat);
            w.Write(graph.Anchor.Placed);
            WriteVec(w, graph.Anchor.Position);
            w.Write(graph.Anchor.Yaw);
            w.Write(graph.NextId);
            w.Write(graph.Revision);
            w.Write((ushort)graph.Count);
            foreach (FluxNode node in graph.Nodes)
            {
                w.Write(node.Id);
                w.Write((byte)node.Kind);
                w.Write(node.Value);
                WriteVec(w, node.Position);
                w.Write((byte)node.Inputs.Length);
                foreach (ushort input in node.Inputs) w.Write(input);
                foreach (double literal in node.Literals) w.Write(literal);
            }
        }

        /// <summary>Reads a graph and checks it is one the owner could have produced.</summary>
        public static FluxGraph ReadGraph(BinaryReader r)
        {
            FluxGraph g = new FluxGraph();
            g.Transport = new FluxTransport
            {
                EpochTicks = r.ReadInt64(),
                Bpm = r.ReadDouble(),
                Dj = r.ReadByte(),
                NextDj = r.ReadByte(),
                SwitchBeat = r.ReadDouble(),
            };
            if (g.Transport.Bpm < FluxGraph.MinBpm || g.Transport.Bpm > FluxGraph.MaxBpm) throw new InvalidDataException("tempo");
            if (g.Transport.Dj >= FluxGraph.DjModes || g.Transport.NextDj >= FluxGraph.DjModes) throw new InvalidDataException("dj");
            FluxAnchor anchor = new FluxAnchor { Placed = r.ReadBoolean(), Position = ReadVec(r), Yaw = r.ReadSingle() };
            if (!anchor.Position.IsFinite || float.IsNaN(anchor.Yaw) || float.IsInfinity(anchor.Yaw)) throw new InvalidDataException("anchor");
            g.Anchor = anchor;
            ushort nextId = r.ReadUInt16();
            uint revision = r.ReadUInt32();
            int count = r.ReadUInt16();
            if (count > FluxGraph.MaxNodes) throw new InvalidDataException("count");

            FluxNode[] nodes = new FluxNode[count];
            for (int i = 0; i < count; i++)
            {
                ushort id = r.ReadUInt16();
                byte kind = r.ReadByte();
                if (id == FluxGraph.None || !FluxKindInfo.IsKnown(kind)) throw new InvalidDataException("node");
                double value = r.ReadDouble();
                FluxVec position = ReadVec(r);
                int arity = r.ReadByte();
                if (arity != FluxKindInfo.Of((FluxKind)kind).Arity) throw new InvalidDataException("arity");
                ushort[] inputs = new ushort[arity];
                for (int p = 0; p < arity; p++) inputs[p] = r.ReadUInt16();
                double[] literals = new double[arity];
                for (int p = 0; p < arity; p++)
                {
                    literals[p] = r.ReadDouble();
                    if (double.IsInfinity(literals[p]) && !double.IsInfinity(FluxKindInfo.Of((FluxKind)kind).Ports[p].Default)) throw new InvalidDataException("literal");
                }
                nodes[i] = new FluxNode { Id = id, Kind = (FluxKind)kind, Value = value, Position = position, Inputs = inputs, Literals = literals };
            }

            // Rebuild through the same ops a live session would apply, then wire. A snapshot that would not
            // compile is rejected here instead of breaking the audio thread later.
            foreach (FluxNode n in nodes)
            {
                if (g.TryGet(n.Id, out _)) throw new InvalidDataException("duplicate id");
                if (!n.Position.IsFinite || double.IsNaN(n.Value) || double.IsInfinity(n.Value)) throw new InvalidDataException("node value");
                g.AddNode(n.Id, n.Kind, n.Value, n.Position).Literals = n.Literals;
            }
            foreach (FluxNode n in nodes)
            {
                for (int p = 0; p < n.Inputs.Length; p++)
                {
                    if (n.Inputs[p] == FluxGraph.None) continue;
                    FluxOp connect = FluxOp.Connect(n.Inputs[p], n.Id, p);
                    if (g.Validate(ref connect, 0) != null) throw new InvalidDataException("wire");
                    g.Apply(connect);
                }
            }
            if (nextId < g.NextId) throw new InvalidDataException("next id");
            g.NextId = nextId;
            g.Revision = revision;
            return g;
        }

        private static void WriteOp(BinaryWriter w, in FluxOp op)
        {
            w.Write((byte)op.Type);
            w.Write(op.Node);
            w.Write((byte)op.Kind);
            w.Write(op.Port);
            w.Write(op.Source);
            w.Write(op.Value);
            WriteVec(w, op.Position);
            w.Write(op.Beat);
            w.Write(op.Ticks);
        }

        private static bool TryReadOp(BinaryReader r, out FluxOp op)
        {
            op = new FluxOp
            {
                Type = (FluxOpType)r.ReadByte(),
                Node = r.ReadUInt16(),
                Kind = (FluxKind)r.ReadByte(),
                Port = r.ReadByte(),
                Source = r.ReadUInt16(),
                Value = r.ReadDouble(),
                Position = ReadVec(r),
                Beat = r.ReadDouble(),
                Ticks = r.ReadInt64(),
            };
            return op.Type >= FluxOpType.AddNode && op.Type <= FluxOpType.Inline
                && FluxKindInfo.IsKnown((byte)op.Kind)
                && op.Position.IsFinite && !double.IsNaN(op.Beat) && !double.IsInfinity(op.Beat);
        }

        private static void WriteVec(BinaryWriter w, FluxVec v)
        {
            w.Write(v.X);
            w.Write(v.Y);
            w.Write(v.Z);
        }

        private static FluxVec ReadVec(BinaryReader r) => new FluxVec(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static BinaryReader Reader(byte[] buffer)
        {
            BinaryReader r = new BinaryReader(new MemoryStream(buffer, false), Encoding.UTF8);
            r.ReadByte();
            return r;
        }
    }
}
