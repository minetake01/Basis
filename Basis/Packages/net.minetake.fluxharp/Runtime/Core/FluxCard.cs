using System;

namespace Net.Minetake.FluxHarp
{
    /// <summary>
    /// The shape of a node on the board, ProtoFlux style: a card with its name across the top, its inputs down
    /// the left edge (each with its name, and its number when unwired), its output on the right edge with the
    /// live value beside it. Shared by the layout and the drawing so they cannot disagree.
    ///
    /// <para>The board is a sphere centred on the player. Cards sit on a reading cylinder inside it, an arm's
    /// length round the player's vertical axis, each turned to face that axis.</para>
    /// </summary>
    public static class FluxCard
    {
        public const float Width = 0.17f;
        public const float Header = 0.03f;
        public const float Row = 0.025f;
        public const float Pad = 0.008f;

        /// <summary>Space kept clear between neighbouring cards in a column.</summary>
        public const float Gap = 0.012f;

        /// <summary>Space between columns, where the wires run.</summary>
        public const float Gutter = 0.07f;

        /// <summary>Radius of the reading cylinder round the player.</summary>
        public const float ReadingRadius = 0.72f;

        /// <summary>How far round the player a sheet may wrap each side of straight ahead, in radians.</summary>
        public const double MaxWrap = 2.2;

        public static float Height(FluxKind kind) => Header + Math.Max(1, FluxKindInfo.Of(kind).Arity) * Row + Pad;

        /// <summary>Card-local position of input <paramref name="port"/> (centre of the card is the origin, y up).</summary>
        public static FluxVec Input(FluxKind kind, int port)
        {
            float top = Height(kind) * 0.5f;
            return new FluxVec(-Width * 0.5f, top - Header - Row * (port + 0.5f), 0);
        }

        /// <summary>Card-local position of the output.</summary>
        public static FluxVec Output(FluxKind kind)
        {
            float top = Height(kind) * 0.5f;
            return new FluxVec(Width * 0.5f, top - Header - Row * 0.5f, 0);
        }

        /// <summary>Board-local point at arc length <paramref name="arc"/> (positive to the right) and height <paramref name="height"/>.</summary>
        public static FluxVec OnCylinder(float arc, float height)
        {
            double a = arc / ReadingRadius;
            return new FluxVec((float)(Math.Sin(a) * ReadingRadius), height, (float)(Math.Cos(a) * ReadingRadius));
        }
    }
}
