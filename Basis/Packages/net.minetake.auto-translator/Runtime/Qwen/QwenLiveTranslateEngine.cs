using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Net.Minetake.AutoTranslator.Qwen
{
    public sealed class QwenLiveTranslateEngine : ISpeechTranslationEngine
    {
        private sealed class Speaker
        {
            public Guid Id;
            public bool Retired;
            public readonly PcmResampler Resampler = new PcmResampler();
            public readonly PcmTimeline Audio;
            public readonly QwenTurnAssembler Turn = new QwenTurnAssembler();
            public long Appends, NonSilent, AbsSum;
            public int Peak;
            public readonly Dictionary<string, int> Events = new Dictionary<string, int>();
            public Speaker(long firstSample)
            {
                Audio = new PcmTimeline(1, firstSample);
            }
        }
        private readonly object gate = new object();
        private readonly Dictionary<Guid, Speaker> speakers = new Dictionary<Guid, Speaker>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly string key;
        private readonly Uri endpoint;
        private readonly string language;
        private readonly int maxSpeakers;
        private bool disposed;
        public event Action<CaptionUpdate> Caption;
        public event Action<TranslatedAudioFrame> Audio;
        public event Action<TranslationFault> Fault;
        public event Action<string> Diagnostic;
        public QwenLiveTranslateEngine(string key, string realtimeUrl, string language, int maxSpeakers)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("DashScope APIキーが必要です。");
            if (maxSpeakers < 1 || maxSpeakers > 64) throw new ArgumentOutOfRangeException(nameof(maxSpeakers));
            QwenProtocol.RequireVoiceLanguage(language);
            this.key = key; this.language = language; this.maxSpeakers = maxSpeakers;
            endpoint = QwenProtocol.Endpoint(realtimeUrl);
        }
        public void AddSpeaker(Guid id)
        {
            Speaker speaker;
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(QwenLiveTranslateEngine));
                if (speakers.Count >= maxSpeakers) throw new InvalidOperationException("翻訳対象人数の上限です。");
                speaker = new Speaker((long)(TranslationClock.Now * 16000) - 3200) { Id = id };
                speakers.Add(id, speaker);
            }
            _ = Task.Run(() => RunSpeaker(speaker));
        }
        public void RemoveSpeaker(Guid id)
        {
            lock (gate)
            {
                if (!speakers.TryGetValue(id, out var s)) return;
                speakers.Remove(id); s.Retired = true;
            }
        }
        public void PushAudio(Guid id, AudioFrame frame)
        {
            Speaker failed = null;
            lock (gate)
            {
                if (!speakers.TryGetValue(id, out var s) || s.Retired) return;
                try { s.Resampler.Process(frame, (position, value) => s.Audio.Put(0, position, value)); }
                catch (Exception) { failed = s; }
            }
            if (failed != null) Fail(failed, "音声形式または音声バッファのエラーです。再接続してください。");
        }
        private void Fail(Speaker speaker, string message)
        {
            lock (gate)
            {
                if (speaker.Retired || disposed) return;
                speaker.Retired = true; speakers.Remove(speaker.Id);
            }
            Fault?.Invoke(new TranslationFault(speaker.Id, message));
        }
        private async Task RunSpeaker(Speaker speaker)
        {
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            using (var socket = new ClientWebSocket())
            {
                Task receive = null;
                var created = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var updated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    socket.Options.SetRequestHeader("Authorization", "Bearer " + key);
                    socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                    using (var connect = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                        await socket.ConnectAsync(endpoint, connect.Token).ConfigureAwait(false);
                    receive = Receive(socket, speaker, created, updated, finished, cancel.Token);
                    await AwaitReady(created, cancel.Token).ConfigureAwait(false);
                    var update = Encoding.UTF8.GetBytes(QwenProtocol.SessionUpdate(language));
                    await socket.SendAsync(new ArraySegment<byte>(update), WebSocketMessageType.Text, true, cancel.Token).ConfigureAwait(false);
                    await AwaitReady(updated, cancel.Token).ConfigureAwait(false);
                    var bytes = new byte[3200];
                    lock (gate)
                    {
                        if (speaker.Retired) return;
                        speaker.Audio.AdvanceTo((long)(TranslationClock.Now * 16000));
                    }
                    double next = TranslationClock.Now;
                    while (true)
                    {
                        bool retired;
                        lock (gate)
                        {
                            retired = speaker.Retired;
                            if (!retired) speaker.Audio.Read(bytes, 1600);
                        }
                        if (retired) break;
                        speaker.Appends++;
                        int peak = PeakAmplitude(bytes, out long absSum);
                        if (peak != 0) speaker.NonSilent++;
                        if (peak > speaker.Peak) speaker.Peak = peak;
                        speaker.AbsSum += absSum;
                        if (receive.IsCompleted) { await receive.ConfigureAwait(false); throw new IOException("Unexpected translation end."); }
                        var payload = Encoding.UTF8.GetBytes(QwenProtocol.AppendAudio(bytes));
                        await socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, cancel.Token).ConfigureAwait(false);
                        next += 0.1;
                        double delay = next - TranslationClock.Now;
                        if (delay < -1) throw new IOException("Audio sender cannot keep pace.");
                        if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay), cancel.Token).ConfigureAwait(false);
                    }
                    if (!receive.IsCompleted && socket.State == WebSocketState.Open)
                    {
                        try
                        {
                            var finish = Encoding.UTF8.GetBytes(QwenProtocol.SessionFinish());
                            await socket.SendAsync(new ArraySegment<byte>(finish), WebSocketMessageType.Text, true, cancel.Token).ConfigureAwait(false);
                            await AwaitReady(finished, cancel.Token, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                        }
                        catch (Exception) { }
                    }
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested || speaker.Retired) { }
                catch (Exception ex)
                { Fail(speaker, "Qwen音声翻訳接続に失敗しました。" + Describe(ex)); }
                finally
                {
                    speaker.Retired = true; cancel.Cancel(); socket.Abort();
                    if (receive != null) { try { await receive.ConfigureAwait(false); } catch (Exception) { } }
                    long mean = speaker.Appends == 0 ? 0 : speaker.AbsSum / (speaker.Appends * 1600);
                    Diagnostic?.Invoke("Qwen session: " + speaker.Appends + " appends (" + speaker.NonSilent + " non-silent, peak " +
                        speaker.Peak + ", mean " + mean + " /32768), events: " +
                        string.Join(", ", speaker.Events.Select(e => e.Key + "x" + e.Value)));
                }
            }
        }
        private static int PeakAmplitude(byte[] pcm, out long absSum)
        {
            int peak = 0; absSum = 0;
            for (int i = 0; i + 1 < pcm.Length; i += 2)
            {
                int v = (short)(pcm[i] | pcm[i + 1] << 8);
                if (v < 0) v = -v;
                absSum += v;
                if (v > peak) peak = v;
            }
            return peak;
        }
        private static async Task AwaitReady(TaskCompletionSource<bool> ready, CancellationToken token, TimeSpan? timeout = null)
        {
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                limit.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
                using (limit.Token.Register(() => ready.TrySetCanceled()))
                    await ready.Task.ConfigureAwait(false);
            }
        }
        private async Task Receive(ClientWebSocket socket, Speaker speaker, TaskCompletionSource<bool> created,
            TaskCompletionSource<bool> updated, TaskCompletionSource<bool> finished, CancellationToken token)
        {
            var buffer = new byte[8192];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    using (var message = new MemoryStream())
                    {
                        WebSocketReceiveResult received;
                        do
                        {
                            received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                            if (received.MessageType == WebSocketMessageType.Close)
                                throw new IOException("Translation connection closed.");
                            if (received.MessageType != WebSocketMessageType.Text) throw new IOException("Unexpected translation frame.");
                            message.Write(buffer, 0, received.Count);
                            if (message.Length > 1024 * 1024) throw new IOException("Translation event exceeds size limit.");
                        } while (!received.EndOfMessage);
                        var data = QwenProtocol.Parse(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                        string type = (string)data["type"];
                        string key = type;
                        if (type == "response.done")
                        {
                            string status = (string)(data["response"]?["status"]);
                            if (!string.IsNullOrEmpty(status) && status != "completed") key = type + ":" + status;
                        }
                        if (type == "conversation.item.input_audio_transcription.completed" &&
                            string.IsNullOrWhiteSpace((string)data["transcript"])) key = type + ":empty";
                        lock (gate) speaker.Events[key] = speaker.Events.TryGetValue(key, out var n) ? n + 1 : 1;
                        if (type == "error") throw new IOException(QwenProtocol.ErrorDetail(data));
                        if (type == "session.created") { created.TrySetResult(true); continue; }
                        if (type == "session.updated")
                        {
                            if (QwenProtocol.IsConfiguredSession(data, language)) updated.TrySetResult(true);
                            continue;
                        }
                        if (type == "session.finished") { finished.TrySetResult(true); return; }
                        CaptionUpdate? caption = null;
                        TranslatedAudioFrame? audio = null;
                        lock (gate)
                        {
                            if (speaker.Retired) continue;
                            if (type == "response.audio.delta")
                            {
                                float[] samples = QwenProtocol.AudioDelta(data);
                                audio = new TranslatedAudioFrame(speaker.Id, samples, samples.Length, QwenProtocol.OutputSampleRate);
                            }
                            else caption = speaker.Turn.Accept(speaker.Id, data);
                        }
                        if (caption.HasValue) Caption?.Invoke(caption.Value);
                        if (audio.HasValue) Audio?.Invoke(audio.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                created.TrySetException(ex); updated.TrySetException(ex); finished.TrySetException(ex); throw;
            }
        }
        private static string Describe(Exception ex)
        {
            var parts = new List<string>();
            for (var current = ex; current != null; current = current.InnerException)
            {
                string text = current.Message;
                if (string.IsNullOrWhiteSpace(text)) text = current.GetType().Name;
                if (parts.Count == 0 || parts[parts.Count - 1] != text) parts.Add(text);
            }
            return parts.Count == 0 ? ex.GetType().Name : string.Join(" / ", parts);
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return; disposed = true;
                foreach (var s in speakers.Values) s.Retired = true;
                speakers.Clear(); lifetime.Cancel();
            }
        }
    }
}
