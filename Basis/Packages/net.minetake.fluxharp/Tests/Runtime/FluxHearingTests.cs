using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SteamAudioManager = SteamAudio.SteamAudioManager;
using SteamAudioSource = SteamAudio.SteamAudioSource;

namespace Net.Minetake.FluxHarp.Tests
{
    /// <summary>
    /// What a listener actually hears, through the project's spatializer: a voice is a real point in space,
    /// rendered binaurally, and moving it in the expression moves it between the ears.
    /// </summary>
    public sealed class FluxHearingTests
    {
        private GameObject _listener;
        private GameObject _harp;

        [TearDown]
        public void TearDown()
        {
            if (_harp != null) UnityEngine.Object.Destroy(_harp);
            if (_listener != null) UnityEngine.Object.Destroy(_listener);
        }

        [UnityTest]
        public IEnumerator AVoiceIsHeardBinaurallyWhereItsOrbitPutsIt()
        {
            Assert.AreEqual("Steam Audio Spatializer", AudioSettings.GetSpatializerPluginName());
            // Only this test's instrument may sound.
            FluxHarpInstrument.Despawn();
            foreach (AudioListener other in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) other.enabled = false;
            yield return null;

            // At the height the voice will fly at (y = 3 octaves), so it passes straight beside one ear.
            float height = (float)(FluxSpace.BaseHeight + 3 * FluxSpace.HeightPerOctave);
            _listener = new GameObject("Listener");
            _listener.transform.SetPositionAndRotation(new Vector3(0, height, 0), Quaternion.identity);
            _listener.AddComponent<AudioListener>();
            Ears ears = _listener.AddComponent<Ears>();
            SteamAudioManager.NotifyAudioListenerChanged();

            FluxSession session = new FluxSession((packet, reliable, recipient) => { });
            session.BecomeOwner(null, Now(), FluxPreset.Seed);
            session.Request(FluxOp.SetAnchor(new FluxVec(0, 0, 0), 0), Now());
            ushort voice = 0;
            foreach (FluxNode n in session.Graph.Nodes) if (n.Kind == FluxKind.Voice) voice = n.Id;
            Assert.AreNotEqual(0, voice);
            // A steady 1760 Hz two metres out: low tones barely differ between the ears, by nature not by bug.
            session.Request(FluxOp.Disconnect(voice, 0), Now());
            session.Request(FluxOp.SetLiteral(voice, 0, 3), Now());
            session.Request(FluxOp.SetLiteral(voice, 2, 2), Now());

            _harp = new GameObject("Flux Harp");
            FluxHarpInstrument instrument = _harp.AddComponent<FluxHarpInstrument>();
            instrument.EyeOverride = _listener.transform;
            instrument.Setup(session, Now);

            // A quarter turn is to the right of the anchor's forward, three quarters to the left.
            session.Request(FluxOp.SetLiteral(voice, 1, 0.25), Now());
            yield return Listen(ears);
            Vector2 right = ears.Rms;
            Transform line = _harp.transform.Find("Lines/Flux Line " + voice);
            Assert.IsNotNull(line);
            Assert.Greater(line.position.x, 1.9f, "the sound source is not where the orbit puts it");

            session.Request(FluxOp.SetLiteral(voice, 1, 0.75), Now());
            yield return Listen(ears);
            Vector2 left = ears.Rms;
            Assert.Less(line.position.x, -1.9f, "the sound source does not follow the orbit");

            Debug.Log($"[FluxHearing] voice right: L={right.x:F4} R={right.y:F4}; voice left: L={left.x:F4} R={left.y:F4}");
            Assert.Greater(right.x + right.y, 1e-3f, "the voice is not heard at all");
            Assert.Greater(right.y, right.x * 1.5f, "a voice on the right is not louder in the right ear");
            Assert.Greater(left.x, left.y * 1.5f, "a voice on the left is not louder in the left ear");

            AudioSource source = line.GetComponent<AudioSource>();
            Assert.IsTrue(source.spatialize);
            Assert.IsTrue(source.spatializePostEffects);
            Assert.IsTrue(line.GetComponent<SteamAudioSource>().directBinaural);
        }

        private static long Now() => DateTime.UtcNow.Ticks;

        private static IEnumerator Listen(Ears ears)
        {
            // Let the source move and the HRTF settle, then measure.
            yield return new WaitForSecondsRealtime(0.4f);
            ears.Clear();
            yield return new WaitForSecondsRealtime(1.2f);
        }

        /// <summary>The final mix at the listener, per ear.</summary>
        private sealed class Ears : MonoBehaviour
        {
            private readonly object _lock = new object();
            private double _l, _r;
            private long _frames;

            public void Clear()
            {
                lock (_lock) { _l = _r = 0; _frames = 0; }
            }

            public Vector2 Rms
            {
                get
                {
                    lock (_lock)
                    {
                        if (_frames == 0) return Vector2.zero;
                        return new Vector2((float)Math.Sqrt(_l / _frames), (float)Math.Sqrt(_r / _frames));
                    }
                }
            }

            private void OnAudioFilterRead(float[] data, int channels)
            {
                if (channels < 2) return;
                double l = 0, r = 0;
                int frames = data.Length / channels;
                for (int f = 0; f < frames; f++)
                {
                    l += data[f * channels] * data[f * channels];
                    r += data[f * channels + 1] * data[f * channels + 1];
                }
                lock (_lock) { _l += l; _r += r; _frames += frames; }
            }
        }
    }
}
