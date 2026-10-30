using System;
using System.Collections.Generic;

namespace Basis.FluxHarp
{
    /// <summary>
    /// Either a node's output or a plain number. A number is never a node of its own: it is written into the
    /// port that reads it, the way the 12 in "12·frac(x)" is part of the multiplication.
    /// </summary>
    public readonly struct FluxTerm
    {
        public readonly ushort Node;
        public readonly double Literal;

        private FluxTerm(ushort node, double literal)
        {
            Node = node;
            Literal = literal;
        }

        public bool IsNode => Node != FluxGraph.None;

        public static FluxTerm Of(ushort node) => new FluxTerm(node, 0);
        public static FluxTerm Number(double value) => new FluxTerm(FluxGraph.None, value);

        /// <summary>An unwired port that keeps its own default.</summary>
        public static readonly FluxTerm Unset = new FluxTerm(FluxGraph.None, double.NaN);

        public static implicit operator FluxTerm(double value) => Number(value);
    }

    /// <summary>
    /// Writes an expression into a graph as code reads: <c>b.Mul(12, b.Frac(b.X))</c>. Laid out as a
    /// ProtoFlux graph flows, inputs to the left, the line on the right, so a built piece opens readable.
    /// </summary>
    public sealed class FluxBuilder
    {
        private readonly FluxGraph _graph;

        public FluxBuilder(FluxGraph graph)
        {
            _graph = graph;
        }

        /// <summary>The loop position. One node per read, so every use sits beside its reader.</summary>
        public FluxTerm X => FluxTerm.Of(Node(FluxKind.X, 0));

        public FluxTerm Dj => FluxTerm.Of(Node(FluxKind.Dj, 0));

        /// <summary>A number that is its own node, for a value the player is meant to dial.</summary>
        public FluxTerm Knob(double value) => FluxTerm.Of(Node(FluxKind.Const, value));

        public FluxTerm Add(FluxTerm a, FluxTerm b) => Op(FluxKind.Add, a, b);
        public FluxTerm Sub(FluxTerm a, FluxTerm b) => Op(FluxKind.Sub, a, b);
        public FluxTerm Mul(FluxTerm a, FluxTerm b) => Op(FluxKind.Mul, a, b);
        public FluxTerm Div(FluxTerm a, FluxTerm b) => Op(FluxKind.Div, a, b);
        public FluxTerm Mod(FluxTerm a, FluxTerm n) => Op(FluxKind.Mod, a, n);
        public FluxTerm Pow(FluxTerm a, FluxTerm b) => Op(FluxKind.Pow, a, b);
        public FluxTerm Floor(FluxTerm a) => Op(FluxKind.Floor, a);
        public FluxTerm Frac(FluxTerm a) => Op(FluxKind.Frac, a);
        public FluxTerm Exp(FluxTerm a) => Op(FluxKind.Exp, a);
        public FluxTerm Log2(FluxTerm a) => Op(FluxKind.Log2, a);
        public FluxTerm Sin(FluxTerm a) => Op(FluxKind.Sin, a);
        public FluxTerm Cos(FluxTerm a) => Op(FluxKind.Cos, a);
        public FluxTerm Abs(FluxTerm a) => Op(FluxKind.Abs, a);
        public FluxTerm Min(FluxTerm a, FluxTerm b) => Op(FluxKind.Min, a, b);
        public FluxTerm Max(FluxTerm a, FluxTerm b) => Op(FluxKind.Max, a, b);
        public FluxTerm Clamp(FluxTerm a, FluxTerm lo, FluxTerm hi) => Op(FluxKind.Clamp, a, lo, hi);
        public FluxTerm Lt(FluxTerm a, FluxTerm b) => Op(FluxKind.Lt, a, b);
        public FluxTerm Eq(FluxTerm a, FluxTerm b) => Op(FluxKind.Eq, a, b);
        public FluxTerm And(FluxTerm a, FluxTerm b) => Op(FluxKind.And, a, b);
        public FluxTerm Or(FluxTerm a, FluxTerm b) => Op(FluxKind.Or, a, b);

        /// <summary>Desmos' <c>{cond: then}</c>; with no else the line is undefined, i.e. silent, outside it.</summary>
        public FluxTerm If(FluxTerm cond, FluxTerm then) => Op(FluxKind.If, cond, then);
        public FluxTerm If(FluxTerm cond, FluxTerm then, FluxTerm @else) => Op(FluxKind.If, cond, then, @else);

        /// <summary>A line that sounds. Unset turn and r leave it riding the playhead around the ring.</summary>
        public ushort Voice(FluxTerm y) => Op(FluxKind.Voice, y).Node;
        public ushort Voice(FluxTerm y, FluxTerm turn) => Op(FluxKind.Voice, y, turn).Node;
        public ushort Voice(FluxTerm y, FluxTerm turn, FluxTerm radius) => Op(FluxKind.Voice, y, turn, radius).Node;

        public ushort Drum(FluxDrumType type, FluxTerm envelope) => Drum(type, envelope, FluxTerm.Unset);

        public ushort Drum(FluxDrumType type, FluxTerm envelope, FluxTerm turn)
        {
            ushort id = Node(FluxKind.Drum, (double)type);
            Wire(id, envelope, turn);
            return id;
        }

        private FluxTerm Op(FluxKind kind, params FluxTerm[] inputs)
        {
            ushort id = Node(kind, 0);
            Wire(id, inputs);
            return FluxTerm.Of(id);
        }

        private void Wire(ushort id, params FluxTerm[] inputs)
        {
            FluxNode node = _graph.Get(id);
            if (inputs.Length > node.Inputs.Length) throw new ArgumentException($"{node.Kind} takes {node.Inputs.Length} inputs.");
            for (int p = 0; p < inputs.Length; p++)
            {
                if (inputs[p].IsNode) node.Inputs[p] = inputs[p].Node;
                else if (!double.IsNaN(inputs[p].Literal)) node.Literals[p] = inputs[p].Literal;
            }
        }

        private ushort Node(FluxKind kind, double value)
        {
            ushort id = _graph.NextId;
            _graph.AddNode(id, kind, value, default);
            return id;
        }

        /// <summary>
        /// Lays the expression out the way a ProtoFlux graph reads: the lines in the rightmost column, each
        /// node one column left of the leftmost node it feeds, so every wire runs left to right. Within a column
        /// nodes are ordered by where their readers sit and stacked by card height, so cards never overlap and
        /// a node sits beside what it feeds. The whole sheet is then centred on the board and curved gently
        /// back at its edges, the inside of the sphere facing the player.
        /// </summary>
        public static void Layout(FluxGraph graph)
        {
            Dictionary<ushort, int> depth = new Dictionary<ushort, int>();
            Dictionary<ushort, List<ushort>> readers = new Dictionary<ushort, List<ushort>>();
            foreach (FluxNode node in graph.Nodes)
            {
                foreach (ushort input in node.Inputs)
                {
                    if (input == FluxGraph.None) continue;
                    if (!readers.TryGetValue(input, out List<ushort> list)) readers[input] = list = new List<ushort>();
                    list.Add(node.Id);
                }
            }
            int maxDepth = 0;
            foreach (FluxNode node in graph.Nodes) maxDepth = Math.Max(maxDepth, DepthOf(node.Id, readers, depth));

            List<FluxNode>[] columns = new List<FluxNode>[maxDepth + 1];
            for (int d = 0; d <= maxDepth; d++) columns[d] = new List<FluxNode>();
            foreach (FluxNode node in graph.Nodes) columns[depth[node.Id]].Add(node);

            Dictionary<ushort, float> y = new Dictionary<ushort, float>();
            float tallest = 0;
            for (int d = 0; d <= maxDepth; d++)
            {
                List<FluxNode> column = columns[d];
                // Each node wants to sit level with the port it feeds; lines keep their creation order.
                Dictionary<ushort, float> want = new Dictionary<ushort, float>();
                for (int i = 0; i < column.Count; i++)
                {
                    FluxNode node = column[i];
                    // Nothing reads it (a line): keep creation order and let the stacking pack them.
                    float target = -i * 1e-3f;
                    if (readers.TryGetValue(node.Id, out List<ushort> rs))
                    {
                        float sum = 0;
                        int n = 0;
                        foreach (ushort r in rs)
                        {
                            if (!y.TryGetValue(r, out float ry)) continue;
                            FluxNode reader = graph.Get(r);
                            for (int p = 0; p < reader.Inputs.Length; p++)
                            {
                                if (reader.Inputs[p] != node.Id) continue;
                                sum += ry + FluxCard.Input(reader.Kind, p).Y - FluxCard.Output(node.Kind).Y;
                                n++;
                            }
                        }
                        if (n > 0) target = sum / n;
                    }
                    want[node.Id] = target;
                }
                column.Sort((a, b) => want[b.Id].CompareTo(want[a.Id]));

                // Stack downward without overlap, then slide the stack so it sits on its wanted heights on average.
                float cursor = float.PositiveInfinity;
                float shift = 0;
                foreach (FluxNode node in column)
                {
                    float h = FluxCard.Height(node.Kind);
                    float top = Math.Min(want[node.Id] + h * 0.5f, cursor);
                    y[node.Id] = top - h * 0.5f;
                    cursor = top - h - FluxCard.Gap;
                    shift += want[node.Id] - y[node.Id];
                }
                shift /= Math.Max(1, column.Count);
                foreach (FluxNode node in column) y[node.Id] += shift;
                if (column.Count > 0)
                {
                    float hi = y[column[0].Id] + FluxCard.Height(column[0].Kind) * 0.5f;
                    float lo = y[column[column.Count - 1].Id] - FluxCard.Height(column[column.Count - 1].Kind) * 0.5f;
                    tallest = Math.Max(tallest, hi - lo);
                }
            }

            // Centre the sheet, and fit it inside the board if it is larger, keeping its proportions.
            float colStep = FluxCard.Width + FluxCard.Gutter;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (FluxNode node in graph.Nodes)
            {
                float h = FluxCard.Height(node.Kind) * 0.5f;
                minY = Math.Min(minY, y[node.Id] - h);
                maxY = Math.Max(maxY, y[node.Id] + h);
            }
            float midY = (minY + maxY) * 0.5f;
            SheetHeight = maxY - minY;
            // The sheet wraps round the player at arm's length: its width runs as an arc of the reading cylinder,
            // its height stays upright, so a long expression surrounds you instead of shrinking past reading.
            // Columns close up (never past touching) when the piece is wider than the arc allows.
            float capacity = (float)(2 * FluxCard.MaxWrap * FluxCard.ReadingRadius);
            float span = maxDepth * colStep;
            if (span > capacity) colStep = Math.Max(FluxCard.Width + 0.004f, capacity / Math.Max(1, maxDepth));
            SheetWidth = maxDepth * colStep + FluxCard.Width;
            float limit = FluxGraph.BoardRadius * 0.98f;
            foreach (FluxNode node in graph.Nodes)
            {
                float arc = (maxDepth * 0.5f - depth[node.Id]) * colStep;
                FluxVec p = FluxCard.OnCylinder(arc, y[node.Id] - midY);
                float len = p.Length;
                if (len > limit) p = new FluxVec(p.X * limit / len, p.Y * limit / len, p.Z * limit / len);
                node.Position = p;
            }
        }

        /// <summary>Extent of the last layout, in metres.</summary>
        public static float SheetWidth { get; private set; }
        public static float SheetHeight { get; private set; }

        /// <summary>Columns from the right: a line is column 0, a node is one left of its leftmost reader.</summary>
        private static int DepthOf(ushort id, Dictionary<ushort, List<ushort>> readers, Dictionary<ushort, int> memo)
        {
            if (memo.TryGetValue(id, out int known)) return known;
            int d = 0;
            if (readers.TryGetValue(id, out List<ushort> rs))
            {
                foreach (ushort r in rs) d = Math.Max(d, DepthOf(r, readers, memo) + 1);
            }
            memo[id] = d;
            return d;
        }
    }
}
