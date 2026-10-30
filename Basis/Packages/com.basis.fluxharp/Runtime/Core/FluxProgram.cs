using System;
using System.Collections.Generic;

namespace Basis.FluxHarp
{
    /// <summary>A line of the expression as the player hears and sees it.</summary>
    public readonly struct FluxOutput
    {
        public readonly ushort Node;
        public readonly FluxKind Kind;

        /// <summary>Drum type for a drum slot.</summary>
        public readonly FluxDrumType Drum;

        /// <summary>Register of each port, or −1 for an unwired port.</summary>
        public readonly int Main;
        public readonly int Theta;
        public readonly int Radius;

        /// <summary>What each port reads when it is unwired.</summary>
        public readonly double MainLiteral;
        public readonly double ThetaLiteral;
        public readonly double RadiusLiteral;

        /// <summary>Instructions this line depends on, in evaluation order.</summary>
        public readonly int[] Slice;

        public FluxOutput(ushort node, FluxKind kind, FluxDrumType drum, int main, int theta, int radius,
            double mainLiteral, double thetaLiteral, double radiusLiteral, int[] slice)
        {
            Node = node;
            Kind = kind;
            Drum = drum;
            Main = main;
            Theta = theta;
            Radius = radius;
            MainLiteral = mainLiteral;
            ThetaLiteral = thetaLiteral;
            RadiusLiteral = radiusLiteral;
            Slice = slice;
        }

        public double ReadMain(double[] registers) => Main < 0 ? MainLiteral : registers[Main];
        public double ReadTheta(double[] registers) => Theta < 0 ? ThetaLiteral : registers[Theta];
        public double ReadRadius(double[] registers) => Radius < 0 ? RadiusLiteral : registers[Radius];
    }

    /// <summary>
    /// An immutable, compiled form of a <see cref="FluxGraph"/>: value nodes in dependency order, one register
    /// each, plus every line's dependency slice. Built on the main thread and handed to the audio thread whole,
    /// so the two never share anything mutable.
    /// </summary>
    public sealed class FluxProgram
    {
        public readonly uint Revision;
        public readonly int RegisterCount;
        public readonly ushort[] NodeOfRegister;
        public readonly FluxOutput[] Voices;
        public readonly FluxOutput[] Drums;

        private readonly FluxKind[] _kind;
        private readonly double[] _value;
        private readonly int[] _in0, _in1, _in2;
        private readonly double[] _def0, _def1, _def2;
        private readonly Dictionary<ushort, int> _registerOfNode;

        public static readonly FluxProgram Empty = Compile(new FluxGraph());

        private FluxProgram(uint revision, int count)
        {
            Revision = revision;
            RegisterCount = count;
            NodeOfRegister = new ushort[count];
            _kind = new FluxKind[count];
            _value = new double[count];
            _in0 = new int[count];
            _in1 = new int[count];
            _in2 = new int[count];
            _def0 = new double[count];
            _def1 = new double[count];
            _def2 = new double[count];
            _registerOfNode = new Dictionary<ushort, int>(count);
            Voices = Array.Empty<FluxOutput>();
            Drums = Array.Empty<FluxOutput>();
        }

        private FluxProgram(FluxProgram shell, FluxOutput[] voices, FluxOutput[] drums)
        {
            Revision = shell.Revision;
            RegisterCount = shell.RegisterCount;
            NodeOfRegister = shell.NodeOfRegister;
            _kind = shell._kind;
            _value = shell._value;
            _in0 = shell._in0;
            _in1 = shell._in1;
            _in2 = shell._in2;
            _def0 = shell._def0;
            _def1 = shell._def1;
            _def2 = shell._def2;
            _registerOfNode = shell._registerOfNode;
            Voices = voices;
            Drums = drums;
        }

        public bool TryGetRegister(ushort node, out int register) => _registerOfNode.TryGetValue(node, out register);

        public static FluxProgram Compile(FluxGraph graph)
        {
            List<FluxNode> order = new List<FluxNode>();
            Dictionary<ushort, byte> mark = new Dictionary<ushort, byte>();
            foreach (FluxNode node in graph.Nodes)
            {
                if (!node.Info.IsOutput) Visit(graph, node, mark, order);
            }

            FluxProgram p = new FluxProgram(graph.Revision, order.Count);
            for (int r = 0; r < order.Count; r++)
            {
                p.NodeOfRegister[r] = order[r].Id;
                p._registerOfNode[order[r].Id] = r;
            }
            for (int r = 0; r < order.Count; r++)
            {
                FluxNode node = order[r];
                FluxPort[] ports = node.Info.Ports;
                p._kind[r] = node.Kind;
                p._value[r] = node.Value;
                p._in0[r] = ports.Length > 0 ? p.RegisterOf(node.Inputs[0]) : -1;
                p._in1[r] = ports.Length > 1 ? p.RegisterOf(node.Inputs[1]) : -1;
                p._in2[r] = ports.Length > 2 ? p.RegisterOf(node.Inputs[2]) : -1;
                p._def0[r] = ports.Length > 0 ? node.Literals[0] : 0;
                p._def1[r] = ports.Length > 1 ? node.Literals[1] : 0;
                p._def2[r] = ports.Length > 2 ? node.Literals[2] : 0;
            }

            List<FluxOutput> voices = new List<FluxOutput>();
            List<FluxOutput> drums = new List<FluxOutput>();
            foreach (FluxNode node in graph.Nodes)
            {
                if (node.Kind == FluxKind.Voice)
                {
                    int y = p.RegisterOf(node.Inputs[0]);
                    int theta = p.RegisterOf(node.Inputs[1]);
                    int radius = p.RegisterOf(node.Inputs[2]);
                    voices.Add(new FluxOutput(node.Id, node.Kind, FluxDrumType.Kick, y, theta, radius,
                        node.Literals[0], node.Literals[1], node.Literals[2], p.SliceOf(y, theta, radius)));
                }
                else if (node.Kind == FluxKind.Drum)
                {
                    int env = p.RegisterOf(node.Inputs[0]);
                    int theta = p.RegisterOf(node.Inputs[1]);
                    drums.Add(new FluxOutput(node.Id, node.Kind, (FluxDrumType)(int)node.Value, env, theta, -1,
                        node.Literals[0], node.Literals[1], double.NaN, p.SliceOf(env, theta, -1)));
                }
            }
            return new FluxProgram(p, voices.ToArray(), drums.ToArray());
        }

        private int RegisterOf(ushort node)
        {
            if (node == FluxGraph.None) return -1;
            if (!_registerOfNode.TryGetValue(node, out int r))
            {
                throw new InvalidOperationException($"Flux node reads {node}, which is not a value node.");
            }
            return r;
        }

        private static void Visit(FluxGraph graph, FluxNode node, Dictionary<ushort, byte> mark, List<FluxNode> order)
        {
            if (mark.TryGetValue(node.Id, out byte state))
            {
                if (state == 1) throw new InvalidOperationException($"Flux expression has a cycle through node {node.Id}.");
                return;
            }
            mark[node.Id] = 1;
            foreach (ushort input in node.Inputs)
            {
                if (input != FluxGraph.None) Visit(graph, graph.Get(input), mark, order);
            }
            mark[node.Id] = 2;
            order.Add(node);
        }

        private int[] SliceOf(int a, int b, int c)
        {
            bool[] need = new bool[RegisterCount];
            Stack<int> pending = new Stack<int>();
            if (a >= 0) pending.Push(a);
            if (b >= 0) pending.Push(b);
            if (c >= 0) pending.Push(c);
            while (pending.Count > 0)
            {
                int r = pending.Pop();
                if (need[r]) continue;
                need[r] = true;
                if (_in0[r] >= 0) pending.Push(_in0[r]);
                if (_in1[r] >= 0) pending.Push(_in1[r]);
                if (_in2[r] >= 0) pending.Push(_in2[r]);
            }
            List<int> slice = new List<int>();
            for (int r = 0; r < RegisterCount; r++)
            {
                if (need[r]) slice.Add(r);
            }
            return slice.ToArray();
        }

        /// <summary>Evaluates every value node at loop position <paramref name="x"/>.</summary>
        public void EvaluateAll(double x, int dj, double[] registers)
        {
            for (int r = 0; r < RegisterCount; r++) Step(r, x, dj, registers);
        }

        /// <summary>Evaluates only what <paramref name="slice"/> needs. Allocation-free; safe on the audio thread.</summary>
        public void Evaluate(int[] slice, double x, int dj, double[] registers)
        {
            for (int i = 0; i < slice.Length; i++) Step(slice[i], x, dj, registers);
        }

        private void Step(int r, double x, int dj, double[] reg)
        {
            double a = _in0[r] >= 0 ? reg[_in0[r]] : _def0[r];
            double b = _in1[r] >= 0 ? reg[_in1[r]] : _def1[r];
            double c = _in2[r] >= 0 ? reg[_in2[r]] : _def2[r];
            reg[r] = Apply(_kind[r], _value[r], a, b, c, x, dj);
        }

        /// <summary>
        /// Desmos semantics: NaN is "undefined" and propagates through arithmetic; comparisons involving it are
        /// false; a piecewise branch that is not taken and has no else is undefined, which is silence.
        /// </summary>
        public static double Apply(FluxKind kind, double value, double a, double b, double c, double x, int dj)
        {
            switch (kind)
            {
                case FluxKind.Const: return value;
                case FluxKind.X: return x;
                case FluxKind.Dj: return dj;
                case FluxKind.Add: return a + b;
                case FluxKind.Sub: return a - b;
                case FluxKind.Mul: return a * b;
                case FluxKind.Div: return b == 0 ? double.NaN : a / b;
                case FluxKind.Mod: return b == 0 ? double.NaN : a - b * Math.Floor(a / b);
                case FluxKind.Pow: return Math.Pow(a, b);
                case FluxKind.Floor: return Math.Floor(a);
                case FluxKind.Frac: return a - Math.Floor(a);
                case FluxKind.Exp: return Math.Exp(a);
                case FluxKind.Log2: return a > 0 ? Math.Log(a, 2) : double.NaN;
                case FluxKind.Sin: return Math.Sin(a);
                case FluxKind.Cos: return Math.Cos(a);
                case FluxKind.Abs: return Math.Abs(a);
                case FluxKind.Min: return Math.Min(a, b);
                case FluxKind.Max: return Math.Max(a, b);
                case FluxKind.Clamp: return Math.Max(b, Math.Min(c, a));
                case FluxKind.Lt: return a < b ? 1 : 0;
                case FluxKind.Eq: return Math.Abs(a - b) < 1e-9 ? 1 : 0;
                case FluxKind.And: return Truthy(a) && Truthy(b) ? 1 : 0;
                case FluxKind.Or: return Truthy(a) || Truthy(b) ? 1 : 0;
                case FluxKind.If: return Truthy(a) ? b : c;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a value node.");
            }
        }

        public static bool Truthy(double v) => !double.IsNaN(v) && v != 0;
    }
}
