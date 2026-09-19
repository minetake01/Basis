using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Net.Minetake.AutoTranslator
{
    public static class TranslationClock
    {
        public static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    }

    // A new Guid for every admission; a Basis network ID may be reused after departure.
    public readonly struct AudioFrame
    {
        public readonly double StartTime;
        public readonly float[] Samples;
        public readonly int Count, Channels, SampleRate;
        public AudioFrame(double startTime, float[] samples, int count, int channels, int sampleRate)
        { StartTime = startTime; Samples = samples; Count = count; Channels = channels; SampleRate = sampleRate; }
    }

    public readonly struct TranscriptUpdate
    {
        public readonly Guid Speaker;
        public readonly long Utterance, Revision;
        public readonly string Text;
        public readonly bool Final;
        public TranscriptUpdate(Guid speaker, long utterance, long revision, string text, bool final)
        { Speaker = speaker; Utterance = utterance; Revision = revision; Text = text; Final = final; }
    }

    public readonly struct CaptionUpdate
    {
        public readonly TranscriptUpdate Transcript;
        public readonly string Translation;
        public CaptionUpdate(TranscriptUpdate transcript, string translation)
        { Transcript = transcript; Translation = translation; }
    }

    public readonly struct TranslationFault
    {
        public readonly Guid Speaker;
        public readonly string Message;
        public TranslationFault(Guid speaker, string message) { Speaker = speaker; Message = message; }
    }

    public interface IStreamingTranscriber : IDisposable
    {
        event Action<TranscriptUpdate> Transcript;
        event Action<TranslationFault> Fault;
        void AddSpeaker(Guid speaker);
        void RemoveSpeaker(Guid speaker);
        // Consumes/copies the samples before returning. Never called on the audio thread.
        void PushAudio(Guid speaker, AudioFrame frame);
    }

    public interface ITextTranslator : IDisposable
    {
        Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellation);
    }

    public interface ISpeechTranslationEngine : IDisposable
    {
        event Action<CaptionUpdate> Caption;
        event Action<TranslationFault> Fault;
        void AddSpeaker(Guid speaker);
        void RemoveSpeaker(Guid speaker);
        void PushAudio(Guid speaker, AudioFrame frame);
    }
}
