using System;

namespace Net.Minetake.FluxHarp
{
    /// <summary>
    /// How a line becomes a place. The Desmos graph is rolled into a cylinder around the instrument:
    /// x (the loop) runs around the ring, y (pitch in octaves) runs up it. A voice with θ and r unwired is
    /// exactly the playhead dot of the flat graph, riding the ring at its pitch's height; wiring θ (turns)
    /// or r (metres) lets the expression fly it anywhere.
    /// </summary>
    public static class FluxSpace
    {
        /// <summary>Ring radius a voice rides at when r is unwired.</summary>
        public const double RingRadius = 2.4;

        /// <summary>Height of y = 0 (220 Hz) above the anchor: about ear height for a standing player.</summary>
        public const double BaseHeight = 1.45;

        /// <summary>Metres per octave on the ring.</summary>
        public const double HeightPerOctave = 0.55;

        public const double MinHeight = 0.05;
        public const double MaxHeight = 4.5;
        public const double MaxRadius = 12;

        /// <summary>Drum slots sit lower and closer, in front unless θ moves them.</summary>
        public const double DrumRadius = 1.3;
        public const double DrumHeight = 0.9;

        /// <summary>The playhead as turns around the ring: 0 at the loop start, 1 at its end.</summary>
        public static double Playhead(double x) => (x - FluxTransport.XMin) / (FluxTransport.XMax - FluxTransport.XMin);

        /// <summary>
        /// Anchor-local position (x right, y up, z forward) for an angle in turns, a radius and a height.
        /// Turn 0 is straight ahead; turns go clockwise seen from above, the way the graph reads left to right.
        /// </summary>
        public static FluxVec Polar(double turns, double radius, double height)
        {
            double a = turns * 2 * Math.PI;
            double r = Math.Max(0, Math.Min(MaxRadius, radius));
            double h = Math.Max(MinHeight, Math.Min(MaxHeight, height));
            return new FluxVec((float)(Math.Sin(a) * r), (float)h, (float)(Math.Cos(a) * r));
        }

        public static FluxVec VoicePosition(double y, double theta, double radius, double x)
        {
            double turns = double.IsNaN(theta) ? Playhead(x) : theta;
            double r = double.IsNaN(radius) ? RingRadius : radius;
            double h = double.IsNaN(y) ? BaseHeight : BaseHeight + y * HeightPerOctave;
            return Polar(turns, r, h);
        }

        public static FluxVec DrumPosition(double theta) =>
            Polar(double.IsNaN(theta) ? 0 : theta, DrumRadius, DrumHeight);

        /// <summary>Frequency of y, in hertz. Raw: y is never snapped to any scale.</summary>
        public static double Frequency(double y) => FluxSound.ReferenceHz * Math.Pow(2, y);
    }
}
