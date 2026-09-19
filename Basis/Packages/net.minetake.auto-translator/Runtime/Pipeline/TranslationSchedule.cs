using System;

namespace Net.Minetake.AutoTranslator
{
    /// <summary>One latest pending revision per speaker. The engine serializes access.</summary>
    public sealed class TranslationSchedule
    {
        public TranscriptUpdate Latest { get; private set; }
        public bool Pending { get; private set; }
        public double LastRequest { get; private set; } = double.NegativeInfinity;
        public bool Offer(TranscriptUpdate update)
        {
            if (update.Utterance < Latest.Utterance || update.Revision <= Latest.Revision) return false;
            Latest = update; Pending = true; return true;
        }
        public double Delay(double now) => Latest.Final ? 0 : Math.Max(0, 1 - (now - LastRequest));
        public TranscriptUpdate Take(double now)
        {
            if (!Pending) throw new InvalidOperationException("No translation is pending.");
            Pending = false; LastRequest = now; return Latest;
        }
    }
}
