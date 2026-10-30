namespace Basis.FluxHarp
{
    /// <summary>The byte value is the wire format, so types are only ever appended.</summary>
    public enum FluxOpType : byte
    {
        AddNode = 1,
        RemoveNode = 2,
        MoveNode = 3,
        Connect = 4,
        SetValue = 5,
        SetDj = 6,
        SetTempo = 7,
        LoadPreset = 8,
        SetAnchor = 9,
        SetLiteral = 10,
        Extract = 11,
        Inline = 12,
    }

    /// <summary>
    /// One edit to the shared expression. Requests carry what the player asked for; the owner fills in
    /// what only it may decide (a new node's id, the bar a DJ switch lands on, the shared-clock instant a
    /// tempo change pivots around) before committing.
    /// </summary>
    public struct FluxOp
    {
        public FluxOpType Type;
        public ushort Node;
        public FluxKind Kind;
        public byte Port;
        public ushort Source;

        /// <summary>Constant / drum type (SetValue, AddNode), bpm (SetTempo), preset (LoadPreset), yaw in degrees (SetAnchor).</summary>
        public double Value;

        /// <summary>Node position in the node sphere, or the instrument's world position (SetAnchor).</summary>
        public FluxVec Position;

        /// <summary>SetDj: the bar head the switch lands on.</summary>
        public double Beat;

        /// <summary>SetTempo, SetAnchor: the shared-clock instant the owner committed at.</summary>
        public long Ticks;

        public static FluxOp Add(FluxKind kind, FluxVec position, double value = 0) =>
            new FluxOp { Type = FluxOpType.AddNode, Kind = kind, Position = position, Value = value };

        public static FluxOp Remove(ushort node) => new FluxOp { Type = FluxOpType.RemoveNode, Node = node };

        public static FluxOp Move(ushort node, FluxVec position) =>
            new FluxOp { Type = FluxOpType.MoveNode, Node = node, Position = position };

        public static FluxOp Connect(ushort source, ushort node, int port) =>
            new FluxOp { Type = FluxOpType.Connect, Source = source, Node = node, Port = (byte)port };

        public static FluxOp Disconnect(ushort node, int port) =>
            new FluxOp { Type = FluxOpType.Connect, Source = FluxGraph.None, Node = node, Port = (byte)port };

        public static FluxOp SetValue(ushort node, double value) =>
            new FluxOp { Type = FluxOpType.SetValue, Node = node, Value = value };

        /// <summary>Sets the number an unwired port reads; NaN returns it to the port's default.</summary>
        public static FluxOp SetLiteral(ushort node, int port, double value) =>
            new FluxOp { Type = FluxOpType.SetLiteral, Node = node, Port = (byte)port, Value = value };

        /// <summary>
        /// Lifts the number written in an unwired port out into a number node of its own at
        /// <paramref name="position"/>, wired back into that port. The owner picks the new node's id.
        /// </summary>
        public static FluxOp Extract(ushort node, int port, FluxVec position) =>
            new FluxOp { Type = FluxOpType.Extract, Node = node, Port = (byte)port, Position = position };

        /// <summary>
        /// Writes number node <paramref name="number"/>'s value into a port as its literal, unwiring the port.
        /// A number node left feeding nothing goes with it.
        /// </summary>
        public static FluxOp Inline(ushort number, ushort node, int port) =>
            new FluxOp { Type = FluxOpType.Inline, Source = number, Node = node, Port = (byte)port };

        public static FluxOp SetDj(int mode) => new FluxOp { Type = FluxOpType.SetDj, Port = (byte)mode };

        public static FluxOp SetTempo(double bpm) => new FluxOp { Type = FluxOpType.SetTempo, Value = bpm };

        public static FluxOp LoadPreset(FluxPreset preset) => new FluxOp { Type = FluxOpType.LoadPreset, Value = (double)preset };

        public static FluxOp SetAnchor(FluxVec worldPosition, float yawDegrees) =>
            new FluxOp { Type = FluxOpType.SetAnchor, Position = worldPosition, Value = yawDegrees };
    }
}
