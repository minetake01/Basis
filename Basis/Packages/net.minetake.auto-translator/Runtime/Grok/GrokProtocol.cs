using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Net.Minetake.AutoTranslator.Grok
{
    public static class GrokProtocol
    {
        public static Uri CreateUri(int channels)
        {
            if (channels < 2 || channels > 8) throw new ArgumentOutOfRangeException(nameof(channels));
            return new Uri("wss://api.x.ai/v1/stt?model=grok-voice-transcribe-2.0&encoding=pcm&sample_rate=16000" +
                "&multichannel=true&channels=" + channels.ToString(CultureInfo.InvariantCulture) + "&interim_results=true&endpointing=400");
        }
        public static JObject Parse(string json)
        {
            var data = JObject.Parse(json);
            if (data["type"]?.Type != JTokenType.String) throw new FormatException("Missing event type.");
            return data;
        }
        public static int Channel(JObject data, int count)
        {
            if (data["channel_index"]?.Type != JTokenType.Integer) throw new FormatException("Missing channel index.");
            int index = (int)data["channel_index"];
            if (index < 0 || index >= count) throw new FormatException("Invalid channel index.");
            return index;
        }
    }

    /// <summary>Immutable slot identity until the entire connection is retired.</summary>
    public sealed class ChannelAssignments
    {
        private readonly Guid[] assigned;
        private readonly bool[] active;
        public int Capacity => assigned.Length;
        public int ActiveCount { get; private set; }
        public ChannelAssignments(int capacity) { assigned = new Guid[capacity]; active = new bool[capacity]; }
        public int Add(Guid speaker)
        {
            for (int i = 0; i < assigned.Length; i++)
                if (assigned[i] == Guid.Empty) { assigned[i] = speaker; active[i] = true; ActiveCount++; return i; }
            return -1;
        }
        public void Remove(Guid speaker)
        {
            for (int i = 0; i < assigned.Length; i++)
                if (assigned[i] == speaker && active[i]) { active[i] = false; ActiveCount--; }
        }
        public Guid GetActive(int index) => active[index] ? assigned[index] : Guid.Empty;
        public IEnumerable<Guid> Active()
        { for (int i = 0; i < assigned.Length; i++) if (active[i]) yield return assigned[i]; }
    }

    public sealed class TranscriptAssembler
    {
        private readonly SortedDictionary<double, string> chunks = new SortedDictionary<double, string>();
        private double completedEnd = double.NegativeInfinity;
        private string lastText = "";
        private long utterance, revision;
        public void NewConnection()
        { chunks.Clear(); lastText = ""; completedEnd = double.NegativeInfinity; utterance++; }
        public TranscriptUpdate? Accept(Guid speaker, JObject data)
        {
            string type = (string)data["type"];
            // done is session-wide history, not a new utterance; speech_final owns captions.
            if (type != "transcript.partial") return null;
            if (data["text"]?.Type != JTokenType.String || data["start"] == null || data["duration"] == null ||
                data["is_final"]?.Type != JTokenType.Boolean || data["speech_final"]?.Type != JTokenType.Boolean)
                throw new FormatException("Incomplete transcript event.");
            string text = ((string)data["text"]).Trim();
            double start = (double)data["start"], end = start + (double)data["duration"];
            bool final = (bool)data["speech_final"], locked = (bool)data["is_final"];
            if (end <= completedEnd || text.Length == 0) return null;
            if (final)
            {
                // The utterance-final text is already stitched by the server.
                var update = new TranscriptUpdate(speaker, utterance++, ++revision, text, true);
                completedEnd = end; chunks.Clear(); lastText = ""; return update;
            }
            var parts = new List<string>();
            foreach (var pair in chunks) if (pair.Key < start) parts.Add(pair.Value);
            parts.Add(text);
            string combined = string.Join(" ", parts);
            if (locked) chunks[start] = text;
            if (combined == lastText) return null;
            lastText = combined;
            return new TranscriptUpdate(speaker, utterance, ++revision, combined, false);
        }
    }
}
