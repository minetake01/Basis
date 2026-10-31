using System;

namespace Net.Minetake.FluxHarp
{
    /// <summary>
    /// What the audio thread needs to render: an immutable program and transport, swapped in whole by the
    /// main thread. Nothing in it is ever mutated after it is published.
    /// </summary>
    public sealed class FluxScore
    {
        public readonly FluxProgram Program;
        public readonly FluxTransport Transport;

        public FluxScore(FluxProgram program, FluxTransport transport)
        {
            Program = program ?? throw new ArgumentNullException(nameof(program));
            Transport = transport;
        }

        public static readonly FluxScore Silent = new FluxScore(FluxProgram.Empty, FluxTransport.Default);
    }

    /// <summary>
    /// Renders one line of the expression. The line is evaluated at a control rate (every
    /// <see cref="ControlFrames"/> samples) and the pitch is interpolated between, so a glide written as
    /// e^(−14·frac(2x)) bends exactly as drawn. Pitch is the raw float y: no scale, no snapping.
    /// </summary>
    public sealed class FluxSound
    {
        public const double ReferenceHz = 220;
        public const int ControlFrames = 16;
        public const double MinHz = 16;
        public const double MaxHz = 9000;

        private const double AttackSeconds = 0.004;
        private const double ReleaseSeconds = 0.03;
        private const float VoiceLevel = 0.1f;
        private const float DrumLevel = 0.3f;

        private readonly double[] _registers = new double[FluxGraph.MaxNodes];
        private double _phase;
        private double _hz = ReferenceHz;
        private double _gain;
        private uint _noise = 0x9E3779B9u;
        private double _hatHistory;
        private double _kickPhase;

        /// <summary>
        /// Fills <paramref name="mono"/> with <paramref name="frames"/> samples of the line identified by
        /// <paramref name="node"/>, starting at shared-clock time <paramref name="startTicks"/>. A line that no
        /// longer exists, or is undefined, fades to silence.
        /// </summary>
        public void Render(FluxScore score, ushort node, long startTicks, int sampleRate, float[] mono, int frames)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            FluxProgram program = score.Program;
            FluxTransport transport = score.Transport;

            if (!TryFind(program, node, out FluxOutput line))
            {
                Fade(mono, frames, sampleRate);
                return;
            }

            double beatsPerSample = transport.BeatsPerSecond / sampleRate;
            double startBeat = transport.BeatAt(startTicks);
            double attack = 1.0 / (AttackSeconds * sampleRate);
            double release = 1.0 / (ReleaseSeconds * sampleRate);

            for (int block = 0; block < frames; block += ControlFrames)
            {
                int n = Math.Min(ControlFrames, frames - block);
                double beat = startBeat + (block + n) * beatsPerSample;
                double x = FluxTransport.XOf(beat);
                int dj = transport.DjAt(beat);
                program.Evaluate(line.Slice, x, dj, _registers);
                double value = line.ReadMain(_registers);

                if (line.Kind == FluxKind.Voice)
                {
                    RenderVoice(value, mono, block, n, sampleRate, attack, release);
                }
                else
                {
                    RenderDrum(line.Drum, value, mono, block, n, sampleRate);
                }
            }
        }

        private void RenderVoice(double y, float[] mono, int offset, int n, int sampleRate, double attack, double release)
        {
            bool sounding = !double.IsNaN(y) && !double.IsInfinity(y);
            double targetHz = sounding ? Math.Max(MinHz, Math.Min(MaxHz, FluxSpace.Frequency(y))) : _hz;
            double startHz = _gain <= 0 ? targetHz : _hz;
            // Lower lines are louder, as on a real instrument, so the bass is not lost under the arpeggio.
            double level = VoiceLevel * (sounding ? 1.0 / Math.Sqrt(Math.Max(0.35, 1.0 + 0.6 * y)) : 1.0);

            for (int i = 0; i < n; i++)
            {
                double t = (i + 1.0) / n;
                double hz = startHz + (targetHz - startHz) * t;
                _gain = sounding ? Math.Min(1, _gain + attack) : Math.Max(0, _gain - release);
                _phase += hz / sampleRate;
                _phase -= Math.Floor(_phase);
                double p = _phase * 2 * Math.PI;
                // A soft, slightly hollow tone: fundamental, a little octave and twelfth, a breath of the
                // fifth harmonic, rolling off as the pitch climbs so the top stays glassy.
                double bright = Math.Max(0, 1 - hz / 2400);
                double s = Math.Sin(p) + bright * (0.32 * Math.Sin(2 * p) + 0.16 * Math.Sin(3 * p) + 0.06 * Math.Sin(5 * p));
                mono[offset + i] = (float)(s * _gain * level);
            }
            _hz = targetHz;
            _lastLevel = level;
        }

        private double _lastLevel = VoiceLevel;

        private void RenderDrum(FluxDrumType type, double envelope, float[] mono, int offset, int n, int sampleRate)
        {
            double env = double.IsNaN(envelope) ? 0 : Math.Max(0, Math.Min(1.5, envelope));
            for (int i = 0; i < n; i++)
            {
                double s;
                switch (type)
                {
                    case FluxDrumType.Kick:
                        // The envelope also bends the pitch, the e^(−26·frac(x)) drop of the reference's kick line.
                        _kickPhase += (42 + 140 * env * env) / sampleRate;
                        _kickPhase -= Math.Floor(_kickPhase);
                        s = Math.Sin(_kickPhase * 2 * Math.PI) * env * 1.2;
                        break;
                    case FluxDrumType.Snare:
                        _kickPhase += 185.0 / sampleRate;
                        _kickPhase -= Math.Floor(_kickPhase);
                        s = (0.65 * Noise() + 0.35 * Math.Sin(_kickPhase * 2 * Math.PI)) * env;
                        break;
                    case FluxDrumType.Hat:
                    {
                        double w = Noise();
                        s = (w - _hatHistory) * 0.5 * env;
                        _hatHistory = w;
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(type));
                }
                mono[offset + i] = (float)(s * DrumLevel);
            }
            _gain = 0;
        }

        private void Fade(float[] mono, int frames, int sampleRate)
        {
            double release = 1.0 / (ReleaseSeconds * sampleRate);
            for (int i = 0; i < frames; i++)
            {
                _gain = Math.Max(0, _gain - release);
                _phase += _hz / sampleRate;
                _phase -= Math.Floor(_phase);
                mono[i] = (float)(Math.Sin(_phase * 2 * Math.PI) * _gain * _lastLevel);
            }
        }

        private double Noise()
        {
            _noise ^= _noise << 13;
            _noise ^= _noise >> 17;
            _noise ^= _noise << 5;
            return _noise / (double)uint.MaxValue * 2 - 1;
        }

        public static bool TryFind(FluxProgram program, ushort node, out FluxOutput line)
        {
            foreach (FluxOutput v in program.Voices)
            {
                if (v.Node == node)
                {
                    line = v;
                    return true;
                }
            }
            foreach (FluxOutput d in program.Drums)
            {
                if (d.Node == node)
                {
                    line = d;
                    return true;
                }
            }
            line = default;
            return false;
        }
    }
}
