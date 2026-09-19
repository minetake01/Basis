using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Net.Minetake.AutoTranslator
{
    public sealed class SpeechTranslationEngine : ISpeechTranslationEngine
    {
        private sealed class Speaker
        {
            public readonly CancellationTokenSource Cancel = new CancellationTokenSource();
            public readonly SemaphoreSlim Wake = new SemaphoreSlim(0, 1);
            public readonly TranslationSchedule Work = new TranslationSchedule();
            public long TranslatedRevision = -1;
            public long TranslatedUtterance = -1;
        }
        private readonly object gate = new object();
        private readonly Dictionary<Guid, Speaker> speakers = new Dictionary<Guid, Speaker>();
        private readonly IStreamingTranscriber transcriber;
        private readonly ITextTranslator translator;
        private readonly string language;
        private readonly SemaphoreSlim requests = new SemaphoreSlim(4, 4);
        private bool disposed;
        public event Action<CaptionUpdate> Caption;
#pragma warning disable CS0067
        public event Action<TranslatedAudioFrame> Audio;
#pragma warning restore CS0067
        public event Action<TranslationFault> Fault;
        public SpeechTranslationEngine(IStreamingTranscriber transcriber, ITextTranslator translator, string language)
        {
            this.transcriber = transcriber; this.translator = translator; this.language = language;
            transcriber.Transcript += OnTranscript; transcriber.Fault += OnFault;
        }
        public void AddSpeaker(Guid id)
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(SpeechTranslationEngine));
                var speaker = new Speaker(); speakers.Add(id, speaker);
                try { transcriber.AddSpeaker(id); }
                catch { speakers.Remove(id); speaker.Cancel.Dispose(); throw; }
                _ = RunSpeaker(id, speaker);
            }
        }
        public void RemoveSpeaker(Guid id)
        {
            lock (gate)
            {
                if (!speakers.TryGetValue(id, out var s)) return;
                speakers.Remove(id); s.Cancel.Cancel(); transcriber.RemoveSpeaker(id);
            }
        }
        public void PushAudio(Guid id, AudioFrame frame)
        {
            lock (gate) if (disposed || !speakers.ContainsKey(id)) return;
            transcriber.PushAudio(id, frame);
        }
        private void OnTranscript(TranscriptUpdate update)
        {
            lock (gate)
            {
                if (!speakers.TryGetValue(update.Speaker, out var s)) return;
                if (!s.Work.Offer(update)) return;
                Caption?.Invoke(new CaptionUpdate(update, null));
                if (s.Wake.CurrentCount == 0) s.Wake.Release();
            }
        }
        private void OnFault(TranslationFault fault)
        {
            RemoveSpeaker(fault.Speaker);
            Fault?.Invoke(fault);
        }
        private async Task RunSpeaker(Guid id, Speaker s)
        {
            var token = s.Cancel.Token;
            try
            {
                while (true)
                {
                    await s.Wake.WaitAsync(token).ConfigureAwait(false);
                    while (true)
                    {
                        TranscriptUpdate update;
                        double wait;
                        lock (gate)
                        {
                            if (!s.Work.Pending) break;
                            wait = s.Work.Delay(TranslationClock.Now);
                        }
                        // Wake early when a final result replaces an interim request.
                        if (wait > 0) { await s.Wake.WaitAsync(TimeSpan.FromSeconds(wait), token).ConfigureAwait(false); continue; }
                        await requests.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            lock (gate) { update = s.Work.Take(TranslationClock.Now); }
                            string result = await translator.TranslateAsync(update.Text, language, token).ConfigureAwait(false);
                            lock (gate)
                            {
                                if (token.IsCancellationRequested || s.Work.Latest.Utterance != update.Utterance) continue;
                                if (s.TranslatedUtterance == update.Utterance && s.TranslatedRevision >= update.Revision) continue;
                                s.TranslatedUtterance = update.Utterance; s.TranslatedRevision = update.Revision;
                                // Keep the newest original text while an older interim translation completes.
                                Caption?.Invoke(new CaptionUpdate(s.Work.Latest, result));
                            }
                        }
                        finally { requests.Release(); }
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception)
            {
                // Never expose provider exception bodies, which may echo text or credentials.
                OnFault(new TranslationFault(id, "翻訳要求に失敗しました。API設定を確認して再接続してください。"));
            }
            finally { s.Wake.Dispose(); s.Cancel.Dispose(); }
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return; disposed = true;
                transcriber.Transcript -= OnTranscript; transcriber.Fault -= OnFault;
                foreach (var s in speakers.Values) s.Cancel.Cancel();
                speakers.Clear();
            }
            transcriber.Dispose(); translator.Dispose();
        }
    }
}
