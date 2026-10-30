using System;

namespace Basis.FluxHarp
{
    /// <summary>
    /// The shared musical clock. Beat 0 is <see cref="EpochTicks"/> on the shared (server) clock; x, the
    /// loop position every line is a function of, is the beat folded into [−8, 8): four bars of 4/4.
    /// </summary>
    public struct FluxTransport
    {
        public const double TicksPerSecond = TimeSpan.TicksPerSecond;
        public const int BeatsPerBar = 4;
        public const int LoopBeats = 16;
        public const double XMin = -8;
        public const double XMax = 8;
        public const double DefaultBpm = 136;

        public long EpochTicks;
        public double Bpm;

        /// <summary>DJ mode in force before <see cref="SwitchBeat"/>.</summary>
        public byte Dj;

        /// <summary>DJ mode from <see cref="SwitchBeat"/> on.</summary>
        public byte NextDj;

        /// <summary>The bar head the pending switch lands on. A switch is a hard cut, never a crossfade.</summary>
        public double SwitchBeat;

        public static FluxTransport Default => new FluxTransport
        {
            EpochTicks = 0,
            Bpm = DefaultBpm,
            Dj = 0,
            NextDj = 0,
            SwitchBeat = 0,
        };

        public static FluxTransport StartingAt(long ticks, double bpm) => new FluxTransport
        {
            EpochTicks = ticks,
            Bpm = bpm,
        };

        public double BeatsPerSecond => Bpm / 60.0;

        public double BeatAt(long ticks) => (ticks - EpochTicks) / TicksPerSecond * BeatsPerSecond;

        public byte DjAt(double beat) => beat >= SwitchBeat ? NextDj : Dj;

        /// <summary>Loop position for a beat: x ∈ [−8, 8).</summary>
        public static double XOf(double beat)
        {
            double folded = beat - Math.Floor(beat / LoopBeats) * LoopBeats;
            return folded + XMin;
        }

        public static bool IsBarHead(double beat) => beat == Math.Floor(beat) && ((long)beat % BeatsPerBar) == 0;

        /// <summary>The next bar head strictly after <paramref name="beat"/>.</summary>
        public static double NextBarHead(double beat) => (Math.Floor(beat / BeatsPerBar) + 1) * BeatsPerBar;

        public FluxTransport WithDj(byte mode, double switchBeat)
        {
            FluxTransport t = this;
            // Whatever was in force when the new switch lands becomes the "before" mode, so a switch queued
            // over a still-pending one keeps the earlier one's effect up to the new bar.
            t.Dj = DjAt(switchBeat);
            t.NextDj = mode;
            t.SwitchBeat = switchBeat;
            return t;
        }

        /// <summary>Changes tempo without moving the beat at <paramref name="pivotTicks"/>.</summary>
        public FluxTransport WithTempo(double bpm, long pivotTicks)
        {
            double beat = BeatAt(pivotTicks);
            FluxTransport t = this;
            t.Bpm = bpm;
            t.EpochTicks = pivotTicks - (long)Math.Round(beat * 60.0 / bpm * TicksPerSecond);
            return t;
        }
    }
}
