using System;
using System.IO;
using Net.Minetake.AutoTranslator.OpenAI;
using Newtonsoft.Json;

namespace Net.Minetake.AutoTranslator.BasisIntegration
{
    [Serializable]
    public sealed class TranslatorConfiguration
    {
        public bool Enabled;
        public string TargetLanguage = "日本語";
        public string TranslationBaseUrl = "";
        public string TranslationModel = "";
        public int MaxSpeakers = 8;
        public void Validate()
        {
            if (MaxSpeakers < 1 || MaxSpeakers > 64) throw new ArgumentException("最大話者数は1〜64人で指定してください。");
            if (string.IsNullOrWhiteSpace(TargetLanguage) || TargetLanguage.Length > 80)
                throw new ArgumentException("翻訳先言語を80文字以内で指定してください。");
            if (Enabled)
            {
                ChatProtocol.Endpoint(TranslationBaseUrl);
                if (string.IsNullOrWhiteSpace(TranslationModel)) throw new ArgumentException("翻訳モデル名が必要です。");
            }
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
