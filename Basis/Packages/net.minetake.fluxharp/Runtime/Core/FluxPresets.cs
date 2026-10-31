using System;

namespace Net.Minetake.FluxHarp
{
    public enum FluxPreset
    {
        /// <summary>The demo piece: a Shasavistic functional progression, translated across dimensions by the DJ cubes.</summary>
        Yxeni = 0,

        /// <summary>The circle-of-fourths line in the reference, transcribed node for node.</summary>
        Fourths = 1,

        /// <summary>One voice reading x: the blank page.</summary>
        Seed = 2,
    }

    /// <summary>
    /// Pieces that ship with the instrument. Each is written the way a player builds one: an expression over
    /// x, the DJ mode and numbers, with no hidden machinery behind it. A number is written into the port that
    /// reads it, or, where it is meant to be played with, is a number node of its own. Pitches are raw
    /// ratios; nothing is rounded to a scale.
    /// </summary>
    public static class FluxPresets
    {
        public static readonly FluxPreset[] All = { FluxPreset.Yxeni, FluxPreset.Fourths, FluxPreset.Seed };

        public static bool IsKnown(double value) =>
            value == Math.Floor(value) && value >= 0 && value <= (double)FluxPreset.Seed;

        public static string Name(FluxPreset preset) => preset switch
        {
            FluxPreset.Yxeni => "yxeni",
            FluxPreset.Fourths => "fourths",
            FluxPreset.Seed => "seed",
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };

        public static double Bpm(FluxPreset preset) => preset switch
        {
            FluxPreset.Yxeni => 112,
            FluxPreset.Fourths => 136,
            FluxPreset.Seed => 120,
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };

        public static void Build(FluxPreset preset, FluxGraph graph)
        {
            FluxBuilder b = new FluxBuilder(graph);
            switch (preset)
            {
                case FluxPreset.Yxeni: BuildYxeni(b); break;
                case FluxPreset.Fourths: BuildFourths(b); break;
                case FluxPreset.Seed: b.Voice(b.Mul(b.Knob(0.25), b.Sin(b.X))); break;
                default: throw new ArgumentOutOfRangeException(nameof(preset));
            }
            FluxBuilder.Layout(graph);
        }

        private static double Oct(double ratio) => Math.Log(ratio, 2);

        /// <summary>
        /// Shasavistic theory (シャサフ式音楽理論) as an expression.
        ///
        /// <para>Harmony: the definition chord Ah·Chy·[guest] moves through the functions [O] [&lt;] [&gt;] [O]
        /// — tonic, a 2D descent (×2/3), a 2D ascent (×3/2), home — one per bar. The guest note is where the
        /// dimension lives, and the DJ cubes translate it: mode 0 is 5D (Fuzi, 11/6), mode 1 is 4D (My, 7/4),
        /// mode 2 is 3D (Ly, 5/4 an octave up: an ordinary major chord, the twelve-tone world) and mode 3 is the
        /// crana colour, 5D Zy (11/8), with the lead and arpeggio dropped out. Each switch is a hard cut at the
        /// next bar head.</para>
        ///
        /// <para>Melody: chord tones only, the guest note as the common tone through every chord, as the
        /// melody-construction video builds it.</para>
        ///
        /// <para>Space: the three pad tones turn slowly round the listener a third of a turn apart; the arpeggio
        /// spirals out from the centre twice a loop; the lead rides the playhead ring; the snare sits behind, and
        /// the hats circle four times a loop.</para>
        /// </summary>
        private static void BuildYxeni(FluxBuilder b)
        {
            // Bar of the loop, 0..3, and the root it puts in force: [O] 1, [<] 2/3, [>] 3/2, [O] 1.
            FluxTerm bar = b.Floor(b.Div(b.Add(b.X, 8), 4));
            FluxTerm root = b.If(b.Lt(bar, 1), 0,
                b.If(b.Lt(bar, 2), Oct(2.0 / 3.0),
                    b.If(b.Lt(bar, 3), Oct(3.0 / 2.0), 0)));

            // The guest note, translated by the DJ mode.
            FluxTerm dj = b.Dj;
            FluxTerm guest = b.If(b.Lt(dj, 1), Oct(11.0 / 6.0),
                b.If(b.Lt(dj, 2), Oct(7.0 / 4.0),
                    b.If(b.Lt(dj, 3), Oct(5.0 / 2.0), Oct(11.0 / 4.0))));
            FluxTerm full = b.Lt(dj, 3);

            // Bass: the root two octaves down in eighths, each with the e^(−14·frac) pluck on its pitch.
            FluxTerm eighth = b.Frac(b.Mul(2, b.X));
            b.Voice(b.If(b.Lt(eighth, 0.72), b.Add(b.Add(root, -2), b.Mul(0.22, b.Exp(b.Mul(-14, eighth))))), 0, 1.6);

            // Pad: Ah · Chy · guest, turning half a turn per loop, a third of a turn apart. How fast it turns is a
            // number node of its own, there to be dialled.
            FluxTerm spin = b.Div(b.X, b.Knob(32));
            FluxTerm padRoot = b.Add(root, -1);
            b.Voice(padRoot, spin, 3.2);
            b.Voice(b.Add(padRoot, Oct(3.0 / 2.0)), b.Add(spin, 1.0 / 3.0), 3.2);
            b.Voice(b.Add(padRoot, guest), b.Add(spin, 2.0 / 3.0), 3.2);

            // Arpeggio: sixteenths walking the chord with (3k+1) mod 4, dropping every third step — three against
            // four, drifting across the bar line — spiralling out from the centre twice a loop.
            FluxTerm step = b.Floor(b.Mul(4, b.X));
            FluxTerm walk = b.Mod(b.Add(b.Mul(3, step), 1), 4);
            FluxTerm tone = b.If(b.Lt(walk, 1), 0,
                b.If(b.Lt(walk, 2), Oct(3.0 / 2.0),
                    b.If(b.Lt(walk, 3), guest, 1)));
            FluxTerm gate = b.And(b.And(full, b.Lt(b.Frac(b.Mul(4, b.X)), 0.6)), b.Lt(b.Mod(step, 3), 2));
            FluxTerm turns = b.Div(b.Add(b.X, 8), 8);
            b.Voice(b.If(gate, b.Add(root, tone)), turns, b.Add(0.6, b.Mul(2.4, b.Frac(turns))));

            // Lead: the guest note as the common tone, a 2D neighbour on the off-beats, a blip on each attack.
            FluxTerm offBeat = b.Eq(b.Mod(b.Floor(b.X), 2), 1);
            FluxTerm leadTone = b.Add(b.Add(root, 1), b.If(offBeat, Oct(3.0 / 2.0), guest));
            FluxTerm blip = b.Div(b.Exp(b.Mul(-12, b.Frac(b.X))), 12);
            b.Voice(b.If(b.And(full, b.Lt(b.Frac(b.X), 0.8)), b.Add(leadTone, blip)));

            // Drums: four on the floor in front, a backbeat snare behind, hats circling four times a loop.
            b.Drum(FluxDrumType.Kick, b.Exp(b.Mul(-26, b.Frac(b.X))));
            b.Drum(FluxDrumType.Snare, b.If(offBeat, b.Exp(b.Mul(-18, b.Frac(b.X))), 0), 0.5);
            b.Drum(FluxDrumType.Hat, b.If(b.Eq(b.Mod(step, 2), 1), b.Exp(b.Mul(-60, b.Frac(b.Mul(4, b.X)))), 0), b.Mul(4, turns));
        }

        /// <summary>
        /// The circle-of-fourths line from the reference, as one piecewise voice:
        /// <c>y = { frac(x) &lt; 1/4: −2 + ⌊(12·mod(3⌊x/2⌋+5, 7)+2)/7⌋/12 + 2.5e^(−22·frac(x)),
        /// frac(4x) &lt; 0.8: ⌊(12·(mod(3⌊x/2⌋ + 2(mod(⌊4x⌋,4) + mod(⌊x⌋,2)) + 1 − 2⌊x/4⌋, 7) + 2⌊x/4⌋ + 4) + 2)/7⌋/12
        /// + (⌊x/2⌋ = 2)(mod(⌊4x⌋, 8) = 1)/12 + e^(−12·frac(4x))/6 }</c>.
        /// The /7 and ⌊⌋ are the composer's own diatonic mapping, written in the expression, not a tuning.
        /// </summary>
        private static void BuildFourths(FluxBuilder b)
        {
            FluxTerm halfFloor = b.Floor(b.Div(b.X, 2));
            FluxTerm quarterFloor = b.Floor(b.Div(b.X, 4));
            FluxTerm fourFloor = b.Floor(b.Mul(4, b.X));

            FluxTerm degree0 = b.Mod(b.Add(b.Mul(3, halfFloor), 5), 7);
            FluxTerm bassPitch = b.Div(b.Floor(b.Div(b.Add(b.Mul(12, degree0), 2), 7)), 12);
            FluxTerm kick = b.Mul(2.5, b.Exp(b.Mul(-22, b.Frac(b.X))));
            FluxTerm bass = b.Add(b.Add(-2, bassPitch), kick);

            FluxTerm inner = b.Add(
                b.Add(b.Mul(3, halfFloor), b.Mul(2, b.Add(b.Mod(fourFloor, 4), b.Mod(b.Floor(b.X), 2)))),
                b.Sub(1, b.Mul(2, quarterFloor)));
            FluxTerm degree1 = b.Add(b.Add(b.Mod(inner, 7), b.Mul(2, quarterFloor)), 4);
            FluxTerm arpPitch = b.Div(b.Floor(b.Div(b.Add(b.Mul(12, degree1), 2), 7)), 12);
            FluxTerm accent = b.Div(b.Mul(b.Eq(halfFloor, 2), b.Eq(b.Mod(fourFloor, 8), 1)), 12);
            FluxTerm blip = b.Div(b.Exp(b.Mul(-12, b.Frac(b.Mul(4, b.X)))), 6);
            FluxTerm arp = b.Add(b.Add(arpPitch, accent), blip);

            b.Voice(b.If(b.Lt(b.Frac(b.X), 0.25), bass, b.If(b.Lt(b.Frac(b.Mul(4, b.X)), 0.8), arp)));
        }
    }
}
