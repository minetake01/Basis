using System;
using Basis.Scripts.Settings;
using UnityEngine;
using AudioSettings = UnityEngine.AudioSettings;
#if STEAMAUDIO_ENABLED && !UNITY_WEBGL
using SteamAudio;
#endif

namespace Basis.FluxHarp
{
    /// <summary>
    /// One line of the expression, heard at a point in space. The generator writes the line's mono signal into
    /// a spatialized source and Steam Audio renders it binaurally through the HRTF, configured as Basis
    /// configures remote voices: spatialize, spatialize post-effects, direct binaural. The GameObject itself
    /// is moved by the instrument to wherever the line's orbit puts it, so the sound really travels.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class FluxHarpEmitter : MonoBehaviour
    {
        /// <summary>What every line of one instrument plays from: its score and its clock.</summary>
        public sealed class Stage
        {
            /// <summary>The score the audio thread reads. Swapped whole by the main thread; never mutated.</summary>
            public volatile FluxScore Score = FluxScore.Silent;

            /// <summary>Audio clock to shared clock.</summary>
            public readonly FluxClockSync Clock = new FluxClockSync();
        }

        public ushort Node { get; private set; }

        private readonly FluxSound _sound = new FluxSound();
        private Stage _stage;
        private AudioSource _source;
        private float[] _mono = new float[4096];
        private volatile float _gain;
        private volatile int _sampleRate;
        private volatile bool _bound;

        public static FluxHarpEmitter Create(Transform parent, Stage stage, ushort node)
        {
            GameObject go = new GameObject($"Flux Line {node}");
            go.transform.SetParent(parent, false);

            // The source has to exist, configured, before the generator component, so Unity runs the
            // generator first and the spatializer receives what it writes.
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.clip = KeepAliveClip();
            source.spatialBlend = 1f;
            source.spatialize = true;
            source.spatializePostEffects = true;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 1.2f;
            source.maxDistance = 40f;
            source.priority = 32;
            if (SMModuleAudio.Instance != null) source.outputAudioMixerGroup = SMModuleAudio.Instance.WorldDefaultMixer;

#if STEAMAUDIO_ENABLED && !UNITY_WEBGL
            SteamAudioSource steam = go.AddComponent<SteamAudioSource>();
            steam.directBinaural = true;
            steam.interpolation = HRTFInterpolation.Bilinear;
            steam.perspectiveCorrection = false;
            steam.distanceAttenuation = true;
            steam.distanceAttenuationInput = DistanceAttenuationInput.CurveDriven;
            steam.airAbsorption = true;
            steam.occlusion = false;
            steam.transmission = false;
            steam.reflections = false;
            steam.MarkCacheDirty();
            steam.RefreshAttenuationData();
#endif

            FluxHarpEmitter emitter = go.AddComponent<FluxHarpEmitter>();
            emitter._source = source;
            emitter._stage = stage ?? throw new ArgumentNullException(nameof(stage));
            emitter.Node = node;
            emitter._sampleRate = AudioSettings.outputSampleRate;
            emitter._gain = FluxHarpSettings.Gain;
            emitter._bound = true;
            source.Play();
            return emitter;
        }

        private static AudioClip _keepAlive;

        /// <summary>A silent looping clip, so the source keeps calling the generator. Its contents are never heard.</summary>
        private static AudioClip KeepAliveClip()
        {
            if (_keepAlive != null) return _keepAlive;
            int rate = Mathf.Max(8000, AudioSettings.outputSampleRate);
            _keepAlive = AudioClip.Create("Flux KeepAlive", rate / 10, 1, rate, false);
            return _keepAlive;
        }

        /// <summary>Main thread, per frame: the listener's volume, and a restart if the device dropped the source.</summary>
        public void Tick(float gain)
        {
            _gain = gain;
            if (_source != null && !_source.isPlaying && isActiveAndEnabled) _source.Play();
        }

        private void OnEnable()
        {
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        private void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }

        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            _sampleRate = AudioSettings.outputSampleRate;
            // A device change stops every source.
            if (_source != null && isActiveAndEnabled) _source.Play();
        }

        // Audio thread. The block starts at the DSP time of the previous block's end; the shared clock maps it
        // onto the beat everyone else is hearing.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            int rate = _sampleRate;
            if (!_bound || channels < 1 || rate <= 0)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            int frames = data.Length / channels;
            if (_mono.Length < frames) _mono = new float[frames];

            double dsp = AudioSettings.dspTime;
            Stage stage = _stage;
            long startTicks = stage.Clock.SharedTicksAt(dsp);
            _sound.Render(stage.Score, Node, startTicks, rate, _mono, frames);

            float gain = _gain;
            float peak = 0;
            for (int f = 0; f < frames; f++)
            {
                float v = _mono[f] * gain;
                peak = Math.Max(peak, Math.Abs(v));
                int b = f * channels;
                for (int c = 0; c < channels; c++) data[b + c] = v;
            }
            Peak = Math.Max(Peak, peak);
        }

        /// <summary>The loudest sample this line has generated since it was last read and cleared.</summary>
        public volatile float Peak;
    }
}
