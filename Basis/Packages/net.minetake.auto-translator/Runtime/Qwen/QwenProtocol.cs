using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Net.Minetake.AutoTranslator.Qwen
{
    public static class QwenProtocol
    {
        public const string Model = "qwen3.8-livetranslate-flash-realtime";
        public const string DefaultUrl = "wss://dashscope-intl.aliyuncs.com/api-ws/v1/realtime";
        public const string TurnDetection = "server_vad";
        public const string Voice = "Tina";
        public const int OutputSampleRate = 24000;
        public static readonly string[] VoiceLanguages =
        {
            "ja", "en", "zh", "ko", "de", "fr", "es", "pt", "it", "ru",
            "ar", "id", "th", "vi", "tr", "hi", "ms", "nl", "ur", "nb",
            "sv", "da", "he", "fi", "pl", "is", "cs", "fil", "fa"
        };
        public static readonly string[] VoiceLanguageNames =
        {
            "日本語", "英語", "中国語", "韓国語", "ドイツ語", "フランス語", "スペイン語", "ポルトガル語", "イタリア語", "ロシア語",
            "アラビア語", "インドネシア語", "タイ語", "ベトナム語", "トルコ語", "ヒンディー語", "マレー語", "オランダ語", "ウルドゥー語", "ノルウェー語",
            "スウェーデン語", "デンマーク語", "ヘブライ語", "フィンランド語", "ポーランド語", "アイスランド語", "チェコ語", "フィリピン語", "ペルシア語"
        };
        private static readonly HashSet<string> VoiceLanguageSet = new HashSet<string>(VoiceLanguages, StringComparer.Ordinal);

        public static Uri Endpoint(string baseUrl)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "wss" ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new ArgumentException("Qwenの接続先にはwss://のURLを指定してください。");
            return new Uri(baseUrl.TrimEnd('/') + "?model=" + Model);
        }
        public static bool IsVoiceLanguage(string code) => code != null && VoiceLanguageSet.Contains(code);
        public static void RequireVoiceLanguage(string code)
        {
            if (!IsVoiceLanguage(code)) throw new ArgumentException("音声翻訳の翻訳先言語が未対応です。");
        }
        public static string SessionUpdate(string language)
        {
            RequireVoiceLanguage(language);
            return new JObject
            {
                ["type"] = "session.update",
                ["session"] = new JObject
                {
                    ["output_modalities"] = new JArray("text", "audio"),
                    ["audio"] = new JObject
                    {
                        ["input"] = new JObject
                        {
                            ["turn_detection"] = new JObject { ["type"] = TurnDetection }
                        },
                        ["output"] = new JObject { ["voice"] = Voice }
                    },
                    ["translation"] = new JObject { ["language"] = language }
                }
            }.ToString(Formatting.None);
        }
        public static string SessionFinish()
        {
            return new JObject { ["type"] = "session.finish" }.ToString(Formatting.None);
        }
        public static bool IsConfiguredSession(JObject data, string language)
        {
            RequireVoiceLanguage(language);
            if (data == null || (string)data["type"] != "session.updated") return false;
            var session = data["session"] as JObject;
            if (session == null) return false;
            if ((string)session["translation"]?["language"] != language) return false;
            if (!HasModality(session["output_modalities"], "text") || !HasModality(session["output_modalities"], "audio"))
                return false;
            if ((string)(session["audio"]?["input"]?["turn_detection"]?["type"]) != TurnDetection) return false;
            return (string)(session["audio"]?["output"]?["voice"]) == Voice;
        }
        private static bool HasModality(JToken modalities, string name)
        {
            var array = modalities as JArray;
            if (array == null) return false;
            foreach (var item in array)
                if (item.Type == JTokenType.String && (string)item == name) return true;
            return false;
        }
        public static string AppendAudio(byte[] pcm)
        {
            if (pcm == null || pcm.Length == 0 || pcm.Length % 2 != 0) throw new ArgumentException("Invalid PCM block.");
            return new JObject
            {
                ["type"] = "input_audio_buffer.append",
                ["audio"] = Convert.ToBase64String(pcm)
            }.ToString(Formatting.None);
        }
        public static JObject Parse(string json)
        {
            var data = JObject.Parse(json);
            if (data["type"]?.Type != JTokenType.String) throw new FormatException("Missing event type.");
            return data;
        }
        public static string ErrorDetail(JObject data)
        {
            var error = data["error"] as JObject;
            if (error == null) return "Translation error event.";
            string code = error["code"]?.Type == JTokenType.String ? (string)error["code"] : null;
            string type = error["type"]?.Type == JTokenType.String ? (string)error["type"] : null;
            string message = error["message"]?.Type == JTokenType.String ? (string)error["message"] : error.ToString(Formatting.None);
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(type)) parts.Add(type);
            if (!string.IsNullOrEmpty(code)) parts.Add(code);
            if (!string.IsNullOrEmpty(message)) parts.Add(message);
            return parts.Count == 0 ? "Translation error event." : string.Join(": ", parts);
        }
        public static float[] Pcm16ToFloat(byte[] pcm)
        {
            if (pcm == null || pcm.Length % 2 != 0) throw new FormatException("PCM16 payload is incomplete.");
            var samples = new float[pcm.Length / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = (short)(pcm[i * 2] | pcm[i * 2 + 1] << 8) / 32768f;
            return samples;
        }
        public static float[] AudioDelta(JObject data)
        {
            if (data["delta"]?.Type != JTokenType.String) throw new FormatException("Missing audio delta.");
            return Pcm16ToFloat(Convert.FromBase64String((string)data["delta"]));
        }
    }

    public sealed class QwenTurnAssembler
    {
        private string original = "", translation = "";
        private bool originalFinal, translationFinal;
        private long utterance, revision;
        public CaptionUpdate? Accept(Guid speaker, JObject data)
        {
            string type = (string)data["type"];
            if (type == "conversation.item.input_audio_transcription.delta")
            {
                if (data["delta"]?.Type != JTokenType.String) throw new FormatException("Incomplete transcription delta.");
                string delta = (string)data["delta"];
                if (delta.Length == 0) return null;
                if (originalFinal) ResetTurn();
                original += delta;
                return Caption(speaker, false);
            }
            if (type == "conversation.item.input_audio_transcription.completed")
            {
                if (data["transcript"]?.Type != JTokenType.String) throw new FormatException("Incomplete transcription result.");
                string text = ((string)data["transcript"]).Trim();
                if (text.Length == 0 && original.Length == 0) return null;
                if (text.Length != 0) original = text;
                originalFinal = true;
                return Caption(speaker, translationFinal);
            }
            if (type == "response.audio_transcript.delta")
            {
                if (data["delta"]?.Type != JTokenType.String) throw new FormatException("Incomplete translation delta.");
                string delta = (string)data["delta"];
                if (delta.Length == 0) return null;
                if (translationFinal && originalFinal) ResetTurn();
                translation += delta;
                return Caption(speaker, false);
            }
            if (type == "response.audio_transcript.done")
            {
                if (data["transcript"]?.Type == JTokenType.String)
                {
                    string text = ((string)data["transcript"]).Trim();
                    if (text.Length != 0) translation = text;
                }
                translationFinal = true;
                return Caption(speaker, originalFinal);
            }
            return null;
        }
        private void ResetTurn()
        {
            utterance++; original = ""; translation = ""; originalFinal = false; translationFinal = false;
        }
        private CaptionUpdate Caption(Guid speaker, bool final)
        {
            string translated = translation.Length == 0 ? null : translation;
            return new CaptionUpdate(new TranscriptUpdate(speaker, utterance, ++revision, original, final), translated);
        }
    }
}
