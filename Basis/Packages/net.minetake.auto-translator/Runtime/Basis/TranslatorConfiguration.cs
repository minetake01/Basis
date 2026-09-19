using System;
using System.IO;
using Net.Minetake.AutoTranslator.OpenAI;
using Net.Minetake.AutoTranslator.Qwen;
using Newtonsoft.Json;

namespace Net.Minetake.AutoTranslator.BasisIntegration
{
    public enum TranslationMode
    {
        Captions = 0,
        Voice = 1
    }

    [Serializable]
    public sealed class TranslatorConfiguration
    {
        public bool Enabled;
        public TranslationMode Mode;
        public string TargetLanguage = "日本語";
        public string TranslationBaseUrl = "";
        public string TranslationModel = "";
        public string VoiceLanguage = "ja";
        public string QwenRealtimeUrl = QwenProtocol.DefaultUrl;
        public float OriginalVoiceGain = 0.2f;
        public int MaxSpeakers = 8;
        public TranslatorConfiguration Copy() => (TranslatorConfiguration)MemberwiseClone();
        public void Validate()
        {
            if (MaxSpeakers < 1 || MaxSpeakers > 64) throw new ArgumentException("最大話者数は1〜64人で指定してください。");
            if (OriginalVoiceGain < 0 || OriginalVoiceGain > 1) throw new ArgumentException("元の声の音量は0〜100%で指定してください。");
            if (Mode == TranslationMode.Captions)
            {
                if (string.IsNullOrWhiteSpace(TargetLanguage) || TargetLanguage.Length > 80)
                    throw new ArgumentException("翻訳先言語を80文字以内で指定してください。");
                if (Enabled)
                {
                    ChatProtocol.Endpoint(TranslationBaseUrl);
                    if (string.IsNullOrWhiteSpace(TranslationModel)) throw new ArgumentException("翻訳モデル名が必要です。");
                }
                return;
            }
            if (Mode != TranslationMode.Voice) throw new ArgumentException("翻訳方式が不正です。");
            QwenProtocol.RequireVoiceLanguage(VoiceLanguage);
            if (Enabled) QwenProtocol.Endpoint(QwenRealtimeUrl);
        }
        public static TranslatorConfiguration Load(string folder)
        {
            string path = Path.Combine(folder, "settings.json");
            if (!File.Exists(path)) return new TranslatorConfiguration();
            var config = JsonConvert.DeserializeObject<TranslatorConfiguration>(File.ReadAllText(path));
            if (config == null) throw new InvalidDataException("自動翻訳の設定を読み込めません。");
            config.Validate(); return config;
        }
        public void Save(string folder)
        {
            Validate(); Directory.CreateDirectory(folder);
            SecretStore.AtomicWrite(Path.Combine(folder, "settings.json"), System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this, Formatting.Indented)));
        }
    }
}
