namespace Net.Minetake.AutoTranslator
{
    public sealed class CaptionState
    {
        public long Utterance { get; private set; } = -1;
        public long Revision { get; private set; } = -1;
        public string Original { get; private set; } = "";
        public string Translation { get; private set; } = "";
        public bool Final { get; private set; }
        public double ExpiresAt { get; private set; } = double.PositiveInfinity;
        public bool Apply(CaptionUpdate update, double now)
        {
            var t = update.Transcript;
            if (t.Utterance < Utterance || (t.Utterance == Utterance && t.Revision < Revision)) return false;
            if (t.Utterance != Utterance) Translation = "";
            Utterance = t.Utterance; Revision = t.Revision; Original = t.Text; Final = t.Final;
            if (update.Translation != null) Translation = update.Translation;
            ExpiresAt = Final ? now + 8 : double.PositiveInfinity;
            return true;
        }
    }
}
