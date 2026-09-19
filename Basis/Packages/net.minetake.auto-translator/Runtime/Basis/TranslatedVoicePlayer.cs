using Net.Minetake.AutoTranslator.Qwen;
using UnityEngine;

namespace Net.Minetake.AutoTranslator.BasisIntegration
{
    public sealed class TranslatedVoicePlayer : MonoBehaviour
    {
        private readonly float[] source = new float[4096];
        private PlaybackRing ring;
        private AudioSource speaker;
        private AudioClip clip;
        private int outputRate, sourceCount, sourceIndex;
        private float previous, current;
        private double phase = 1;
        public volatile float Gain = 1;
        public static TranslatedVoicePlayer Create(Transform mouth, AudioSource template, bool announce)
        {
            var go = new GameObject("Translated voice");
            go.transform.SetParent(mouth, false);
            go.transform.localPosition = Vector3.zero;
            go.layer = mouth.gameObject.layer;
            var view = go.AddComponent<TranslatedVoicePlayer>();
            view.outputRate = AudioSettings.outputSampleRate;
            view.ring = new PlaybackRing(QwenProtocol.OutputSampleRate);
            view.speaker = go.AddComponent<AudioSource>();
            view.speaker.playOnAwake = false;
            view.speaker.loop = true;
            view.speaker.volume = 1;
            view.speaker.dopplerLevel = 0;
            view.speaker.spatialize = !announce;
            view.speaker.spatializePostEffects = !announce;
            view.speaker.spatialBlend = announce ? 0 : 1;
            if (template != null)
            {
                view.speaker.minDistance = template.minDistance;
                view.speaker.maxDistance = template.maxDistance;
                view.speaker.rolloffMode = template.rolloffMode;
            }
            view.clip = AudioClip.Create("Translated voice", view.outputRate, 1, view.outputRate, false);
            view.speaker.clip = view.clip;
            view.speaker.Play();
            return view;
        }
        public void Push(TranslatedAudioFrame frame)
        {
            if (frame.SampleRate != QwenProtocol.OutputSampleRate || frame.Count < 0 ||
                frame.Samples == null || frame.Count > frame.Samples.Length)
                throw new System.ArgumentException("Unsupported translated audio format.");
            ring.Write(frame.Samples, frame.Count);
        }
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (ring == null || channels < 1) return;
            double step = (double)QwenProtocol.OutputSampleRate / outputRate;
            float gain = Gain;
            int frames = data.Length / channels;
            for (int i = 0; i < frames; i++)
            {
                phase += step;
                while (phase >= 1)
                {
                    previous = current;
                    if (sourceIndex >= sourceCount)
                    {
                        sourceCount = ring.Read(source, 0, source.Length);
                        sourceIndex = 0;
                    }
                    current = sourceIndex < sourceCount ? source[sourceIndex++] : 0;
                    phase -= 1;
                }
                float sample = (previous + (current - previous) * (float)phase) * gain;
                int offset = i * channels;
                for (int ch = 0; ch < channels; ch++) data[offset + ch] = sample;
            }
        }
        public void RestoreAndDestroy(AudioSource original)
        {
            if (original != null) original.volume = 1;
            Destroy(gameObject);
        }
        private void OnDestroy()
        {
            if (clip != null) Destroy(clip);
        }
    }
}
