using System;
using System.Collections.Generic;

namespace Basis.FluxHarp
{
    public struct FluxVec
    {
        public float X, Y, Z;

        public FluxVec(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);

        public bool IsFinite =>
            !(float.IsNaN(X) || float.IsNaN(Y) || float.IsNaN(Z) || float.IsInfinity(X) || float.IsInfinity(Y) || float.IsInfinity(Z));
    }

    public sealed class FluxNode
    {
        public ushort Id;
        public FluxKind Kind;

        /// <summary>The number of a constant, the drum type of a drum slot. Unused by other kinds.</summary>
        public double Value;

        /// <summary>Where the node floats inside the node sphere, in sphere-local metres.</summary>
        public FluxVec Position;

        /// <summary>Source node id per input port; <see cref="FluxGraph.None"/> makes the port read its literal.</summary>
        public ushort[] Inputs;

        /// <summary>
        /// The number an unwired port reads, written in place the way an expression's literals are: the 12 in
        /// "12·frac(x)" belongs to the multiplication, not to a node of its own.
        /// </summary>
        public double[] Literals;

        public FluxKindInfo Info => FluxKindInfo.Of(Kind);

        public FluxNode Clone() => new FluxNode
        {
            Id = Id,
            Kind = Kind,
            Value = Value,
            Position = Position,
            Inputs = (ushort[])Inputs.Clone(),
            Literals = (double[])Literals.Clone(),
        };
    }

    /// <summary>Where the instrument stands in the world. Everything spatial is relative to it.</summary>
    public struct FluxAnchor
    {
        public FluxVec Position;
        public float Yaw;
        public bool Placed;
    }

    /// <summary>
    /// The one shared expression. Every edit is a <see cref="FluxOp"/>; the session owner validates and
    /// sequences them, everyone (owner included) applies the sequenced stream with <see cref="Apply"/>, so all
    /// copies are the same graph.
    /// </summary>
    public sealed class FluxGraph
    {
        public const ushort None = 0;
        public const int MaxNodes = 256;
        public const int MaxVoices = 16;
        public const int MaxDrums = 6;
        public const int DjModes = 4;
        public const double MinBpm = 30;
        public const double MaxBpm = 300;

        /// <summary>
        /// Node sphere radius in metres. The sphere is centred on the player; a node thrown out of it is gone.
        /// </summary>
        public const float BoardRadius = 1.4f;

        private readonly SortedDictionary<ushort, FluxNode> _nodes = new SortedDictionary<ushort, FluxNode>();

        public ushort NextId = 1;

        /// <summary>Bumped by every edit that changes what is heard; moves do not.</summary>
        public uint Revision;

        public FluxTransport Transport = FluxTransport.Default;
        public FluxAnchor Anchor;

        public IEnumerable<FluxNode> Nodes => _nodes.Values;
        public int Count => _nodes.Count;

        public bool TryGet(ushort id, out FluxNode node) => _nodes.TryGetValue(id, out node);

        public FluxNode Get(ushort id)
        {
            if (!_nodes.TryGetValue(id, out FluxNode node))
            {
                throw new KeyNotFoundException($"Flux node {id} does not exist.");
            }
            return node;
        }

        public FluxGraph Clone()
        {
            FluxGraph copy = new FluxGraph { NextId = NextId, Revision = Revision, Transport = Transport, Anchor = Anchor };
            foreach (FluxNode node in _nodes.Values)
            {
                copy._nodes.Add(node.Id, node.Clone());
            }
            return copy;
        }

        public int CountOutputs(FluxKind kind)
        {
            int n = 0;
            foreach (FluxNode node in _nodes.Values)
            {
                if (node.Kind == kind) n++;
            }
            return n;
        }

        /// <summary>
        /// Owner side: checks a requested edit and fills in what only the owner decides. <paramref name="nowTicks"/>
        /// is the owner's shared-clock time. Returns null when the edit is acceptable, otherwise why not.
        /// </summary>
        public string Validate(ref FluxOp op, long nowTicks)
        {
            switch (op.Type)
            {
                case FluxOpType.AddNode:
                    if (!FluxKindInfo.IsKnown((byte)op.Kind)) return "unknown kind";
                    if (_nodes.Count >= MaxNodes) return "node limit";
                    if (op.Kind == FluxKind.Voice && CountOutputs(FluxKind.Voice) >= MaxVoices) return "voice limit";
                    if (op.Kind == FluxKind.Drum && CountOutputs(FluxKind.Drum) >= MaxDrums) return "drum limit";
                    if (!IsFinite(op.Value)) return "value not finite";
                    if (op.Kind == FluxKind.Drum && !IsDrumType(op.Value)) return "drum type";
                    if (!InBoard(op.Position)) return "outside the node sphere";
                    if (NextId == ushort.MaxValue) return "id space exhausted";
                    op.Node = NextId;
                    return null;

                case FluxOpType.RemoveNode:
                    return _nodes.ContainsKey(op.Node) ? null : "no such node";

                case FluxOpType.MoveNode:
                    if (!_nodes.ContainsKey(op.Node)) return "no such node";
                    return InBoard(op.Position) ? null : "outside the node sphere";

                case FluxOpType.Connect:
                {
                    if (!_nodes.TryGetValue(op.Node, out FluxNode dst)) return "no such node";
                    if (op.Port >= dst.Inputs.Length) return "no such port";
                    if (op.Source == None) return null;
                    if (!_nodes.TryGetValue(op.Source, out FluxNode src)) return "no such source";
                    if (src.Info.IsOutput) return "a line has no value to read";
                    if (Reaches(op.Source, op.Node)) return "cycle";
                    return null;
                }

                case FluxOpType.SetValue:
                {
                    if (!_nodes.TryGetValue(op.Node, out FluxNode node)) return "no such node";
                    if (!IsFinite(op.Value)) return "value not finite";
                    if (node.Kind == FluxKind.Const) return null;
                    if (node.Kind == FluxKind.Drum) return IsDrumType(op.Value) ? null : "drum type";
                    return "node has no value";
                }

                case FluxOpType.SetLiteral:
                {
                    if (!_nodes.TryGetValue(op.Node, out FluxNode node)) return "no such node";
                    if (op.Port >= node.Literals.Length) return "no such port";
                    // NaN puts the port back to its default; anything else must be a number.
                    return double.IsInfinity(op.Value) ? "value not finite" : null;
                }

                case FluxOpType.SetDj:
                    if (op.Port >= DjModes) return "no such mode";
                    // Lands on the head of the bar after the one playing now, never mid-bar, never with a fade.
                    op.Beat = FluxTransport.NextBarHead(Transport.BeatAt(nowTicks));
                    return null;

                case FluxOpType.SetTempo:
                    if (!(op.Value >= MinBpm && op.Value <= MaxBpm)) return "tempo out of range";
                    op.Ticks = nowTicks;
                    return null;

                case FluxOpType.LoadPreset:
                    if (!FluxPresets.IsKnown(op.Value)) return "no such preset";
                    op.Ticks = nowTicks;
                    return null;

                case FluxOpType.SetAnchor:
                    if (!op.Position.IsFinite || !IsFinite(op.Value)) return "anchor not finite";
                    return null;

                default:
                    return "unknown op";
            }
        }

        /// <summary>
        /// Applies a sequenced edit. The owner has already validated it, so a failure here means the copies
        /// have diverged, which skipping the edit would only hide.
        /// </summary>
        public void Apply(in FluxOp op)
        {
            switch (op.Type)
            {
                case FluxOpType.AddNode:
                    AddNode(op.Node, op.Kind, op.Value, op.Position);
                    break;

                case FluxOpType.RemoveNode:
                    if (!_nodes.Remove(op.Node)) throw new InvalidOperationException($"Flux node {op.Node} does not exist.");
                    foreach (FluxNode node in _nodes.Values)
                    {
                        for (int p = 0; p < node.Inputs.Length; p++)
                        {
                            if (node.Inputs[p] == op.Node) node.Inputs[p] = None;
                        }
                    }
                    Revision++;
                    break;

                case FluxOpType.MoveNode:
                    Get(op.Node).Position = op.Position;
                    break;

                case FluxOpType.Connect:
                    Get(op.Node).Inputs[op.Port] = op.Source;
                    Revision++;
                    break;

                case FluxOpType.SetValue:
                    Get(op.Node).Value = op.Value;
                    Revision++;
                    break;

                case FluxOpType.SetLiteral:
                {
                    FluxNode node = Get(op.Node);
                    node.Literals[op.Port] = double.IsNaN(op.Value) ? node.Info.Ports[op.Port].Default : op.Value;
                    Revision++;
                    break;
                }

                case FluxOpType.SetDj:
                    Transport = Transport.WithDj(op.Port, op.Beat);
                    break;

                case FluxOpType.SetTempo:
                    Transport = Transport.WithTempo(op.Value, op.Ticks);
                    break;

                case FluxOpType.LoadPreset:
                {
                    FluxAnchor anchor = Anchor;
                    _nodes.Clear();
                    NextId = 1;
                    FluxPresets.Build((FluxPreset)(int)op.Value, this);
                    // A new piece starts from the top of its loop the moment it is committed.
                    Transport = FluxTransport.StartingAt(op.Ticks, FluxPresets.Bpm((FluxPreset)(int)op.Value));
                    Anchor = anchor;
                    Revision++;
                    break;
                }

                case FluxOpType.SetAnchor:
                    Anchor = new FluxAnchor { Position = op.Position, Yaw = (float)op.Value, Placed = true };
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(op), op.Type, "Unknown flux op.");
            }
        }

        /// <summary>Adds a node with a known id. Used by <see cref="Apply"/> and by preset builders.</summary>
        public FluxNode AddNode(ushort id, FluxKind kind, double value, FluxVec position)
        {
            if (id == None || _nodes.ContainsKey(id)) throw new InvalidOperationException($"Flux node id {id} is not free.");
            FluxPort[] ports = FluxKindInfo.Of(kind).Ports;
            double[] literals = new double[ports.Length];
            for (int p = 0; p < ports.Length; p++) literals[p] = ports[p].Default;
            FluxNode node = new FluxNode
            {
                Id = id,
                Kind = kind,
                Value = value,
                Position = position,
                Inputs = new ushort[ports.Length],
                Literals = literals,
            };
            _nodes.Add(id, node);
            if (id >= NextId) NextId = (ushort)(id + 1);
            Revision++;
            return node;
        }

        /// <summary>Whether <paramref name="to"/> is <paramref name="from"/> or is read, directly or not, by it.</summary>
        public bool Reaches(ushort from, ushort to)
        {
            if (from == to) return true;
            Stack<ushort> pending = new Stack<ushort>();
            HashSet<ushort> seen = new HashSet<ushort>();
            pending.Push(from);
            while (pending.Count > 0)
            {
                ushort id = pending.Pop();
                if (!seen.Add(id) || !_nodes.TryGetValue(id, out FluxNode node)) continue;
                foreach (ushort input in node.Inputs)
                {
                    if (input == None) continue;
                    if (input == to) return true;
                    pending.Push(input);
                }
            }
            return false;
        }

        private static bool InBoard(FluxVec v) => v.IsFinite && v.Length <= BoardRadius;

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private static bool IsDrumType(double v) => v == Math.Floor(v) && v >= 0 && v <= (double)FluxDrumType.Hat;
    }
}
