using Net.Minetake.AutoTranslator.Grok;
using Net.Minetake.AutoTranslator.OpenAI;

namespace Net.Minetake.AutoTranslator.BasisIntegration
{
    // The only place that selects concrete providers and the overall translation algorithm.
    internal static class TranslatorComposition
    {
        public static ISpeechTranslationEngine Create(TranslatorConfiguration config, string speechKey, string translationKey)
        {
            config.Validate();
            var translator = new OpenAiTextTranslator(config.TranslationBaseUrl, config.TranslationModel, translationKey);
            try { return new SpeechTranslationEngine(new GrokTranscriber(speechKey, config.MaxSpeakers), translator, config.TargetLanguage); }
            catch { translator.Dispose(); throw; }
        }
    }
}
