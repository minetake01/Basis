using System;
using System.Threading;

namespace Net.Minetake.AutoTranslator
{
    /// <summary>Single audio-thread producer, single worker consumer. Overflow is latched.</summary>
    public sealed class AudioCaptureBuffer
    {
        private readonly float[][] samples;
        private readonly int[] counts, channels, rates;
        private readonly double[] times;
        private int write, read, overflow;
        private double expectedTime;
        public bool Overflowed => Volatile.Read(ref overflow) != 0;
        public AudioCaptureBuffer(int slots = 64, int maxSamples = 16384)
        {
            samples = new float[slots][];
            for (int i = 0; i < slots; i++) samples[i] = new float[maxSamples];
            counts = new int[slots]; channels = new int[slots]; rates = new int[slots]; times = new double[slots];
        }
        public void Write(float[] data, int channelCount, int sampleRate, double now)
        {
            int next = (write + 1) % samples.Length;
            if (next == Volatile.Read(ref read) || data.Length > samples[write].Length ||
                channelCount < 1 || sampleRate < 1 || data.Length % channelCount != 0)
            { Volatile.Write(ref overflow, 1); return; }
            // Preserve the sample clock across callback scheduling jitter, but preserve genuine gaps.
            double start = Math.Abs(now - expectedTime) < 0.03 ? expectedTime : now;
            Array.Copy(data, samples[write], data.Length);
            counts[write] = data.Length; channels[write] = channelCount; rates[write] = sampleRate; times[write] = start;
            expectedTime = start + (double)data.Length / channelCount / sampleRate;
            Volatile.Write(ref write, next);
        }
        public bool TryPeek(out AudioFrame frame)
        {
            if (read == Volatile.Read(ref write)) { frame = default; return false; }
            frame = new AudioFrame(times[read], samples[read], counts[read], channels[read], rates[read]);
            return true;
        }
        public void Release() => Volatile.Write(ref read, (read + 1) % samples.Length);
    }

    /// <summary>Causal windowed-sinc lowpass with a continuous fractional sample clock.</summary>
    public sealed class PcmResampler
    {
        private const int Taps = 64;
        private readonly float[] history = new float[Taps];
        private long inputIndex;
        private double nextOutput, origin, expected;
        private int rate;
        private float[][] coefficients;
        public void Process(AudioFrame frame, Action<long, float> output)
        {
            if (frame.SampleRate < 16000 || frame.SampleRate > 192000 || frame.Channels < 1 ||
                frame.Count < 0 || frame.Count > frame.Samples.Length || frame.Count % frame.Channels != 0)
                throw new ArgumentException("Unsupported audio format.");
            if (rate != frame.SampleRate || Math.Abs(frame.StartTime - expected) > 0.03)
            {
                rate = frame.SampleRate; origin = frame.StartTime; inputIndex = 0; nextOutput = 0;
                Array.Clear(history, 0, history.Length);
                BuildCoefficients();
            }
            double ratio = (double)rate / 16000;
            int frames = frame.Count / frame.Channels;
            for (int i = 0; i < frames; i++, inputIndex++)
            {
                float mono = 0;
                for (int ch = 0; ch < frame.Channels; ch++) mono += frame.Samples[i * frame.Channels + ch];
                history[inputIndex % Taps] = mono / frame.Channels;
                if (inputIndex < nextOutput) continue;
                double sum = 0;
                int phase = Math.Min(255, Math.Max(0, (int)Math.Round((inputIndex - nextOutput) * 255)));
                var weights = coefficients[phase];
                for (int k = 0; k < Taps; k++)
                {
                    if (inputIndex >= k) sum += history[(inputIndex - k) % Taps] * weights[k];
                }
                output((long)Math.Round((origin + nextOutput / rate) * 16000), (float)sum);
                nextOutput += ratio;
            }
            expected = frame.StartTime + (double)frames / rate;
        }
        private void BuildCoefficients()
        {
            coefficients = new float[256][];
            double cutoff = Math.Min(0.45, 7200.0 / rate);
            for (int phase = 0; phase < 256; phase++)
            {
                var weights = new float[Taps]; double total = 0;
                for (int k = 0; k < Taps; k++)
                {
                    double x = phase / 255.0 - k + (Taps - 1) * 0.5;
                    double sinc = Math.Abs(x) < 1e-9 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * x) / (Math.PI * x);
                    weights[k] = (float)(sinc * (0.5 - 0.5 * Math.Cos(2 * Math.PI * k / (Taps - 1))));
                    total += weights[k];
                }
                for (int k = 0; k < Taps; k++) weights[k] /= (float)total;
                coefficients[phase] = weights;
            }
        }
    }

    /// <summary>Absolute sample positions keep silence and independently arriving channels aligned.</summary>
    public sealed class PcmTimeline
    {
        private readonly float[][] values;
        private readonly long[][] stamps;
        private readonly int capacity;
        public int Channels => values.Length;
        public long Cursor { get; private set; }
        public PcmTimeline(int channels, long firstSample, int capacity = 160000)
        {
            if (channels < 1 || channels > 8) throw new ArgumentOutOfRangeException(nameof(channels));
            this.capacity = capacity; Cursor = firstSample;
            values = new float[channels][]; stamps = new long[channels][];
            for (int ch = 0; ch < channels; ch++)
            {
                values[ch] = new float[capacity]; stamps[ch] = new long[capacity];
                for (int i = 0; i < capacity; i++) stamps[ch][i] = long.MinValue;
            }
        }
        public void Put(int channel, long position, float value)
        {
            if (position < Cursor) return;
            if (position - Cursor >= capacity) throw new InvalidOperationException("Audio buffer exceeded ten seconds.");
            int index = (int)(position % capacity);
            values[channel][index] = value; stamps[channel][index] = position;
        }
        public void ClearChannel(int channel)
        {
            for (int i = 0; i < capacity; i++) stamps[channel][i] = long.MinValue;
        }
        public void CopyUnreadChannelTo(int channel, PcmTimeline destination, int destinationChannel)
        {
            for (int i = 0; i < capacity; i++)
                if (stamps[channel][i] >= Cursor)
                    destination.Put(destinationChannel, stamps[channel][i], values[channel][i]);
        }
        public void Read(byte[] target, int frames)
        {
            if (target.Length != frames * Channels * 2) throw new ArgumentException("Incorrect PCM block size.");
            int offset = 0;
            for (int i = 0; i < frames; i++)
            {
                long position = Cursor + i;
                int index = (int)(position % capacity);
                for (int ch = 0; ch < Channels; ch++)
                {
                    float value = stamps[ch][index] == position ? values[ch][index] : 0;
                    if (float.IsNaN(value) || float.IsInfinity(value)) value = 0;
                    short pcm = (short)Math.Round(Math.Max(-1, Math.Min(1, value)) * 32767);
                    target[offset++] = (byte)pcm; target[offset++] = (byte)(pcm >> 8);
                }
            }
            Cursor += frames;
        }
    }

    /// <summary>Single-producer playback queue. Writes past capacity drop the oldest samples.</summary>
    public sealed class PlaybackRing
    {
        private readonly float[] buffer;
        private readonly object gate = new object();
        private int write, queued;
        public int Capacity => buffer.Length;
        public int Queued { get { lock (gate) return queued; } }
        public PlaybackRing(int sampleRate, double seconds = 3)
        {
            if (sampleRate < 1 || seconds <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            buffer = new float[Math.Max(1, (int)Math.Round(sampleRate * seconds))];
        }
        public void Write(float[] samples, int count)
        {
            if (samples == null || count < 0 || count > samples.Length) throw new ArgumentException("Invalid playback samples.");
            lock (gate)
            {
                for (int i = 0; i < count; i++)
                {
                    buffer[write] = samples[i];
                    write = (write + 1) % buffer.Length;
                    if (queued < buffer.Length) queued++;
                }
            }
        }
        public int Read(float[] destination, int offset, int count)
        {
            if (destination == null || offset < 0 || count < 0 || offset + count > destination.Length)
                throw new ArgumentException("Invalid playback destination.");
            lock (gate)
            {
                int read = (write - queued + buffer.Length) % buffer.Length;
                int n = Math.Min(count, queued);
                for (int i = 0; i < n; i++)
                {
                    destination[offset + i] = buffer[read];
                    read = (read + 1) % buffer.Length;
                }
                queued -= n;
                return n;
            }
        }
    }
}
