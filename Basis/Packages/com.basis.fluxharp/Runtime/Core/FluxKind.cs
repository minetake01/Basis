using System;

namespace Basis.FluxHarp
{
    /// <summary>
    /// Every node the expression can be built from. The byte value is the wire format, so kinds are
    /// only ever appended.
    /// </summary>
    public enum FluxKind : byte
    {
        Const = 0,
        X = 1,
        Dj = 2,
        Add = 3,
        Sub = 4,
        Mul = 5,
        Div = 6,
        Mod = 7,
        Pow = 8,
        Floor = 9,
        Frac = 10,
        Exp = 11,
        Log2 = 12,
        Sin = 13,
        Cos = 14,
        Abs = 15,
        Min = 16,
        Max = 17,
        Clamp = 18,
        Lt = 19,
        Eq = 20,
        And = 21,
        Or = 22,
        If = 23,
        Voice = 24,
        Drum = 25,
    }

    /// <summary>What a drum slot sounds like. Stored in the drum node's value.</summary>
    public enum FluxDrumType : byte
    {
        Kick = 0,
        Snare = 1,
        Hat = 2,
    }

    public readonly struct FluxPort
    {
        public readonly string Name;

        /// <summary>Value an unconnected input reads. NaN is Desmos' "undefined".</summary>
        public readonly double Default;

        public FluxPort(string name, double @default)
        {
            Name = name;
            Default = @default;
        }
    }

    public sealed class FluxKindInfo
    {
        public readonly FluxKind Kind;
        public readonly string Label;
        public readonly FluxPort[] Ports;

        /// <summary>A line of the expression: something that sounds.</summary>
        public readonly bool IsOutput;

        private FluxKindInfo(FluxKind kind, string label, bool isOutput, params FluxPort[] ports)
        {
            Kind = kind;
            Label = label;
            IsOutput = isOutput;
            Ports = ports;
        }

        public int Arity => Ports.Length;

        private static readonly FluxKindInfo[] Table = Build();

        public static FluxKindInfo Of(FluxKind kind)
        {
            int index = (int)kind;
            if (index >= Table.Length || Table[index] == null)
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown flux node kind.");
            }
            return Table[index];
        }

        public static bool IsKnown(byte kind) => kind < Table.Length && Table[kind] != null;

        /// <summary>The kinds offered for creation, in the order the palette shows them.</summary>
        public static readonly FluxKind[] Palette =
        {
            FluxKind.Const, FluxKind.X, FluxKind.Dj,
            FluxKind.Add, FluxKind.Sub, FluxKind.Mul, FluxKind.Div, FluxKind.Mod, FluxKind.Pow,
            FluxKind.Floor, FluxKind.Frac, FluxKind.Exp, FluxKind.Log2, FluxKind.Sin, FluxKind.Cos,
            FluxKind.Abs, FluxKind.Min, FluxKind.Max, FluxKind.Clamp,
            FluxKind.Lt, FluxKind.Eq, FluxKind.And, FluxKind.Or, FluxKind.If,
            FluxKind.Voice, FluxKind.Drum,
        };

        private static FluxKindInfo[] Build()
        {
            const double Undefined = double.NaN;
            FluxKindInfo[] t = new FluxKindInfo[(int)FluxKind.Drum + 1];
            void Add(FluxKindInfo info) => t[(int)info.Kind] = info;

            Add(new FluxKindInfo(FluxKind.Const, "num", false));
            Add(new FluxKindInfo(FluxKind.X, "x", false));
            Add(new FluxKindInfo(FluxKind.Dj, "dj", false));
            Add(new FluxKindInfo(FluxKind.Add, "+", false, new FluxPort("a", 0), new FluxPort("b", 0)));
            Add(new FluxKindInfo(FluxKind.Sub, "-", false, new FluxPort("a", 0), new FluxPort("b", 0)));
            Add(new FluxKindInfo(FluxKind.Mul, "*", false, new FluxPort("a", 1), new FluxPort("b", 1)));
            Add(new FluxKindInfo(FluxKind.Div, "/", false, new FluxPort("a", 1), new FluxPort("b", 1)));
            Add(new FluxKindInfo(FluxKind.Mod, "mod", false, new FluxPort("a", 0), new FluxPort("n", 1)));
            Add(new FluxKindInfo(FluxKind.Pow, "^", false, new FluxPort("a", 1), new FluxPort("b", 1)));
            Add(new FluxKindInfo(FluxKind.Floor, "floor", false, new FluxPort("a", 0)));
            Add(new FluxKindInfo(FluxKind.Frac, "frac", false, new FluxPort("a", 0)));
            Add(new FluxKindInfo(FluxKind.Exp, "exp", false, new FluxPort("a", 0)));
            Add(new FluxKindInfo(FluxKind.Log2, "log2", false, new FluxPort("a", 1)));
            Add(new FluxKindInfo(FluxKind.Sin, "sin", false, new FluxPort("a", 0)));
            Add(new FluxKindInfo(FluxKind.Cos, "cos", false, new FluxPort("a", 0)));
            Add(new FluxKindInfo(FluxKind.Abs, "abs", false, new FluxPort("a", 0)));
            // An unconnected side never wins, so a min/max with one wire passes it straight through.
            Add(new FluxKindInfo(FluxKind.Min, "min", false, new FluxPort("a", double.PositiveInfinity), new FluxPort("b", double.PositiveInfinity)));
            Add(new FluxKindInfo(FluxKind.Max, "max", false, new FluxPort("a", double.NegativeInfinity), new FluxPort("b", double.NegativeInfinity)));
            Add(new FluxKindInfo(FluxKind.Clamp, "clamp", false, new FluxPort("a", 0), new FluxPort("lo", 0), new FluxPort("hi", 1)));
            Add(new FluxKindInfo(FluxKind.Lt, "<", false, new FluxPort("a", 0), new FluxPort("b", 0)));
            Add(new FluxKindInfo(FluxKind.Eq, "=", false, new FluxPort("a", 0), new FluxPort("b", 0)));
            Add(new FluxKindInfo(FluxKind.And, "and", false, new FluxPort("a", 1), new FluxPort("b", 1)));
            Add(new FluxKindInfo(FluxKind.Or, "or", false, new FluxPort("a", 0), new FluxPort("b", 0)));
            Add(new FluxKindInfo(FluxKind.If, "if", false, new FluxPort("cond", 0), new FluxPort("then", Undefined), new FluxPort("else", Undefined)));
            // A line. y is pitch in octaves above 220 Hz, never snapped to a scale; turn (of the ring) and r (metres) bend its orbit.
            // Unconnected turn/r leave the voice on the graph itself: the playhead's angle, the ring's radius.
            Add(new FluxKindInfo(FluxKind.Voice, "voice", true, new FluxPort("y", Undefined), new FluxPort("turn", Undefined), new FluxPort("r", Undefined)));
            // A drum slot. env is the hit's loudness curve over x, e.g. exp(−26·frac(x)).
            Add(new FluxKindInfo(FluxKind.Drum, "drum", true, new FluxPort("env", 0), new FluxPort("turn", Undefined)));
            return t;
        }
    }
}
