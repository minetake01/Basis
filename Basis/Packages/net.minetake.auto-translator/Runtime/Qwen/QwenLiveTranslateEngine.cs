using System;
using System.Collections.Generic;
using System.IO;
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
                try
                {
                    socket.Options.SetRequestHeader("Authorization", "Bearer " + key);
                    socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                    cancel.CancelAfter(TimeSpan.FromSeconds(10));
                    await socket.ConnectAsync(endpoint, cancel.Token).ConfigureAwait(false);
                    var created = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var updated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    receive = Receive(socket, speaker, created, updated, cancel.Token);
                    using (cancel.Token.Register(() => created.TrySetCanceled()))
                        await created.Task.ConfigureAwait(false);
                    var update = Encoding.UTF8.GetBytes(QwenProtocol.SessionUpdate(language));
                    await socket.SendAsync(new ArraySegment<byte>(update), WebSocketMessageType.Text, true, cancel.Token).ConfigureAwait(false);
                    using (cancel.Token.Register(() => updated.TrySetCanceled()))
                        await updated.Task.ConfigureAwait(false);
                    cancel.CancelAfter(Timeout.Infinite);
                    var bytes = new byte[3200];
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
                        if (receive.IsCompleted) { await receive.ConfigureAwait(false); throw new IOException("Unexpected translation end."); }
                        cancel.CancelAfter(TimeSpan.FromSeconds(10));
                        var payload = Encoding.UTF8.GetBytes(QwenProtocol.AppendAudio(bytes));
                        await socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, cancel.Token).ConfigureAwait(false);
                        cancel.CancelAfter(Timeout.Infinite);
                        next += 0.1;
                        double delay = next - TranslationClock.Now;
                        if (delay < -1) throw new IOException("Audio sender cannot keep pace.");
                        if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay), cancel.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested || speaker.Retired) { }
                catch (Exception)
                { Fail(speaker, "Qwen音声翻訳接続に失敗しました。API設定と接続を確認して再接続してください。"); }
                finally
                {
                    speaker.Retired = true; cancel.Cancel(); socket.Abort();
                    if (receive != null) { try { await receive.ConfigureAwait(false); } catch (Exception) { } }
                }
            }
        }
        private async Task Receive(ClientWebSocket socket, Speaker speaker, TaskCompletionSource<bool> created,
            TaskCompletionSource<bool> updated, CancellationToken token)
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
                            if (received.MessageType != WebSocketMessageType.Text) throw new IOException("Unexpected translation frame or close.");
                            message.Write(buffer, 0, received.Count);
                            if (message.Length > 1024 * 1024) throw new IOException("Translation event exceeds size limit.");
                        } while (!received.EndOfMessage);
                        var data = QwenProtocol.Parse(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                        string type = (string)data["type"];
                        if (type == "error") throw new IOException("Translation error event.");
                        if (type == "session.created") { created.TrySetResult(true); continue; }
                        if (type == "session.updated") { updated.TrySetResult(true); continue; }
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
            catch (Exception ex) { created.TrySetException(ex); updated.TrySetException(ex); throw; }
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
