using Net.Minetake.AutoTranslator.Grok;
using Net.Minetake.AutoTranslator.OpenAI;
using Net.Minetake.AutoTranslator.Qwen;

namespace Net.Minetake.AutoTranslator.BasisIntegration
{
    // The only place that selects concrete providers and the overall translation algorithm.
    internal static class TranslatorComposition
    {
        public static ISpeechTranslationEngine Create(TranslatorConfiguration config, string speechKey, string translationKey, string dashscopeKey)
        {
            config.Validate();
            if (config.Mode == TranslationMode.Voice)
                return new QwenLiveTranslateEngine(dashscopeKey, config.QwenRealtimeUrl, config.VoiceLanguage, config.MaxSpeakers);
            var translator = new OpenAiTextTranslator(config.TranslationBaseUrl, config.TranslationModel, translationKey);
            try { return new SpeechTranslationEngine(new GrokTranscriber(speechKey, config.MaxSpeakers), translator, config.TargetLanguage); }
            catch { translator.Dispose(); throw; }
        }
    }
}
