using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Net.Minetake.AutoTranslator.Grok
{
    public sealed class GrokTranscriber : IStreamingTranscriber
    {
        private sealed class Speaker
        {
            public Guid Id;
            public Group Group;
            public int Channel;
            public readonly PcmResampler Resampler = new PcmResampler();
            public readonly TranscriptAssembler Transcript = new TranscriptAssembler();
        }
        private sealed class Group
        {
            public readonly ChannelAssignments Slots;
            public readonly PcmTimeline Audio;
            public bool Retired;
            public Group(int channels, long firstSample)
            {
                Slots = new ChannelAssignments(channels);
                Audio = new PcmTimeline(channels, firstSample);
            }
        }
        private readonly object gate = new object();
        private readonly Dictionary<Guid, Speaker> speakers = new Dictionary<Guid, Speaker>();
        private readonly List<Group> groups = new List<Group>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly string key;
        private readonly int maxSpeakers;
        private bool disposed;
        public event Action<TranscriptUpdate> Transcript;
        public event Action<TranslationFault> Fault;
        public GrokTranscriber(string key, int maxSpeakers)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("xAI APIキーが必要です。");
            if (maxSpeakers < 1 || maxSpeakers > 64) throw new ArgumentOutOfRangeException(nameof(maxSpeakers));
            this.key = key; this.maxSpeakers = maxSpeakers;
        }
        public void AddSpeaker(Guid id)
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(GrokTranscriber));
                if (speakers.Count >= maxSpeakers) throw new InvalidOperationException("文字起こし対象人数の上限です。");
                var s = new Speaker { Id = id };
                foreach (var g in groups)
                {
                    int channel = g.Slots.Add(id);
                    if (channel >= 0) { Attach(s, g, channel); return; }
                }
                // Reclaim tombstones by rotating only the affected group. Identity never changes in-place.
                var old = groups.FirstOrDefault(g => g.Slots.ActiveCount < g.Slots.Capacity);
                if (old != null)
                {
                    var retained = old.Slots.Active().Select(x => speakers[x]).ToArray();
                    old.Retired = true; groups.Remove(old);
                    var replacement = CreateGroup(old.Slots.Capacity, old.Audio.Cursor);
                    foreach (var existing in retained)
                    {
                        existing.Transcript.NewConnection();
                        int previousChannel = existing.Channel;
                        existing.Group = replacement; existing.Channel = replacement.Slots.Add(existing.Id);
                        old.Audio.CopyUnreadChannelTo(previousChannel, replacement.Audio, existing.Channel);
                    }
                    Attach(s, replacement, replacement.Slots.Add(id));
                    return;
                }
                int remaining = maxSpeakers - groups.Sum(g => g.Slots.Capacity);
                var created = CreateGroup(Math.Max(2, Math.Min(8, remaining)));
                Attach(s, created, created.Slots.Add(id));
            }
        }
        private Group CreateGroup(int channels, long? firstSample = null)
        {
            var group = new Group(channels, firstSample ?? (long)(TranslationClock.Now * 16000) - 3200); groups.Add(group);
            _ = Task.Run(() => RunGroup(group));
            return group;
        }
        private void Attach(Speaker s, Group group, int channel)
        { s.Group = group; s.Channel = channel; speakers.Add(s.Id, s); }
        public void RemoveSpeaker(Guid id)
        {
            lock (gate)
            {
                if (!speakers.TryGetValue(id, out var s)) return;
                speakers.Remove(id); s.Group.Slots.Remove(id); s.Group.Audio.ClearChannel(s.Channel);
                if (s.Group.Slots.ActiveCount == 0) { s.Group.Retired = true; groups.Remove(s.Group); }
            }
        }
        public void PushAudio(Guid id, AudioFrame frame)
        {
            Group failed = null;
            lock (gate)
            {
                if (!speakers.TryGetValue(id, out var s) || s.Group.Retired) return;
                try { s.Resampler.Process(frame, (position, value) => s.Group.Audio.Put(s.Channel, position, value)); }
                catch (Exception) { failed = s.Group; }
            }
            if (failed != null) Fail(failed, "音声形式または音声バッファのエラーです。再接続してください。");
        }
        private void Fail(Group group, string message)
        {
            Guid[] affected;
            lock (gate)
            {
                if (group.Retired || disposed) return;
                affected = group.Slots.Active().ToArray();
                group.Retired = true; groups.Remove(group);
                foreach (var id in affected) speakers.Remove(id);
            }
            foreach (var id in affected) Fault?.Invoke(new TranslationFault(id, message));
        }
        private async Task RunGroup(Group group)
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
                    await socket.ConnectAsync(GrokProtocol.CreateUri(group.Slots.Capacity), cancel.Token).ConfigureAwait(false);
                    var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    receive = Receive(socket, group, ready, cancel.Token);
                    using (cancel.Token.Register(() => ready.TrySetCanceled()))
                        await ready.Task.ConfigureAwait(false);
                    cancel.CancelAfter(Timeout.Infinite);
                    var bytes = new byte[1600 * group.Slots.Capacity * 2];
                    double next = TranslationClock.Now;
                    while (true)
                    {
                        bool retired;
                        lock (gate)
                        {
                            retired = group.Retired;
                            if (!retired) group.Audio.Read(bytes, 1600);
                        }
                        if (retired) break;
                        if (receive.IsCompleted) { await receive.ConfigureAwait(false); throw new IOException("Unexpected STT end."); }
                        cancel.CancelAfter(TimeSpan.FromSeconds(10));
                        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, cancel.Token).ConfigureAwait(false);
                        cancel.CancelAfter(Timeout.Infinite);
                        next += 0.1;
                        double delay = next - TranslationClock.Now;
                        if (delay < -1) throw new IOException("Audio sender cannot keep pace.");
                        if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay), cancel.Token).ConfigureAwait(false);
                    }
                    cancel.CancelAfter(TimeSpan.FromSeconds(5));
                    var done = Encoding.UTF8.GetBytes("{\"type\":\"audio.done\"}");
                    await socket.SendAsync(new ArraySegment<byte>(done), WebSocketMessageType.Text, true, cancel.Token).ConfigureAwait(false);
                    await receive.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception)
                { Fail(group, "Grok文字起こし接続に失敗しました。API設定と接続を確認して再接続してください。"); }
                finally
                {
                    cancel.Cancel(); socket.Abort();
                    if (receive != null) { try { await receive.ConfigureAwait(false); } catch (Exception) { } }
                }
            }
        }
        private async Task Receive(ClientWebSocket socket, Group group, TaskCompletionSource<bool> ready, CancellationToken token)
        {
            var buffer = new byte[8192];
            var completed = new HashSet<int>();
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
                            if (received.MessageType != WebSocketMessageType.Text) throw new IOException("Unexpected STT frame or close.");
                            message.Write(buffer, 0, received.Count);
                            if (message.Length > 1024 * 1024) throw new IOException("STT event exceeds size limit.");
                        } while (!received.EndOfMessage);
                        var data = GrokProtocol.Parse(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                        string type = (string)data["type"];
                        if (type == "error") throw new IOException("STT error event.");
                        if (type == "transcript.created") { ready.TrySetResult(true); continue; }
                        if (type != "transcript.partial" && type != "transcript.done") continue;
                        int channel = GrokProtocol.Channel(data, group.Slots.Capacity);
                        if (type == "transcript.done")
                        {
                            completed.Add(channel);
                            if (completed.Count == group.Slots.Capacity) return;
                            continue;
                        }
                        TranscriptUpdate? update = null;
                        lock (gate)
                        {
                            var id = group.Slots.GetActive(channel);
                            if (!group.Retired && id != Guid.Empty && speakers.TryGetValue(id, out var s) && s.Group == group)
                                update = s.Transcript.Accept(id, data);
                        }
                        if (update.HasValue) Transcript?.Invoke(update.Value);
                    }
                }
            }
            catch (Exception ex) { ready.TrySetException(ex); throw; }
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return; disposed = true;
                foreach (var g in groups) g.Retired = true;
                groups.Clear(); speakers.Clear(); lifetime.Cancel();
            }
        }
    }
}
