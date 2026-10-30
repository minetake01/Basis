using System;

namespace Basis.FluxHarp
{
    /// <summary>
    /// Maps the audio device's clock onto the shared (server) clock. The shared clock is resampled from a
    /// ping every second or so and jumps by a few milliseconds each time; the audio clock is smooth but its
    /// own. Following the shared clock through a slewed offset keeps every client on the same beat without
    /// the pitch wobble or clicks a jumping clock would put into the sound. A large disagreement (a server
    /// switch, a stall) is a cut, not a slew.
    /// </summary>
    public sealed class FluxClockSync
    {
        /// <summary>Offsets this far apart are snapped instead of slewed.</summary>
        public const long SnapTicks = TimeSpan.TicksPerMillisecond * 120;

        /// <summary>Fraction of the remaining error corrected per second.</summary>
        public const double SlewPerSecond = 0.8;

        private long _offset;
        private bool _locked;
        private double _lastDsp;

        /// <summary>Shared-clock ticks minus audio-clock ticks.</summary>
        public long Offset => _offset;

        public bool Locked => _locked;

        /// <summary>Main thread, once per frame: the audio clock now and the shared clock now.</summary>
        public void Observe(double dspSeconds, long sharedTicks)
        {
            long target = sharedTicks - (long)(dspSeconds * TimeSpan.TicksPerSecond);
            if (!_locked || Math.Abs(target - _offset) > SnapTicks)
            {
                _offset = target;
                _locked = true;
                _lastDsp = dspSeconds;
                return;
            }
            double dt = Math.Max(0, Math.Min(0.25, dspSeconds - _lastDsp));
            _lastDsp = dspSeconds;
            double k = 1 - Math.Exp(-SlewPerSecond * dt);
            _offset += (long)((target - _offset) * k);
        }

        /// <summary>Any thread: the shared-clock instant of an audio-clock time.</summary>
        public long SharedTicksAt(double dspSeconds) => _offset + (long)(dspSeconds * TimeSpan.TicksPerSecond);
    }
}
