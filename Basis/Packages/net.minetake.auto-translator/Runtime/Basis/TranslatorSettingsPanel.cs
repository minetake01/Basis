using System;
using System.Collections.Generic;
using Basis.BasisUI;
using Net.Minetake.AutoTranslator.Qwen;
using TMPro;
using UnityEngine;

namespace Net.Minetake.AutoTranslator.BasisIntegration
{
    internal static class TranslatorSettingsPanel
    {
        public static PanelTabPage Build(PanelTabGroup tabGroup)
        {
            var host = TranslatorHost.Instance;
            PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor descriptor = tab.Descriptor;
            descriptor.SetIcon(AddressableAssets.Sprites.Settings);
            descriptor.SetTitle("自動翻訳");

            RectTransform container = descriptor.ContentParent;

            PanelElementDescriptor aboutGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            aboutGroup.SetTitle("自動翻訳");
            aboutGroup.SetDescription("字幕方式は周囲の音声をxAIへ、文字起こしを翻訳APIへ送ります。音声翻訳方式はDashScopeのQwen LiveTranslateへ送り、訳を音声でも再生します。結果は自分だけに表示・再生されます。");

            PanelToggle enabled = PanelToggle.CreateNewEntry(aboutGroup.ContentParent);
            enabled.Descriptor.SetTitle("有効にする");
            enabled.Descriptor.SetTooltip("保存して適用を押すまで反映されません。");
            enabled.SetValueWithoutNotify(host.Config.Enabled);

            PanelDropdown mode = PanelDropdown.CreateNewEntry(aboutGroup.ContentParent);
            mode.Descriptor.SetTitle("方式");
            mode.AssignEntries(new List<string> { "captions", "voice" }, new List<string> { "字幕（Grok + 翻訳API）", "音声翻訳（Qwen LiveTranslate）" });
            mode.SetValueWithoutNotify(host.Config.Mode == TranslationMode.Voice ? "voice" : "captions");

            PanelTextField count = TextField(aboutGroup.ContentParent, "最大話者数", host.Config.MaxSpeakers.ToString(), 2);
            count.Descriptor.SetTooltip("1〜64。字幕方式は8チャンネル単位、音声翻訳は話者ごとに接続します。");
            if (count._inputField != null)
            {
                count._inputField.contentType = TMP_InputField.ContentType.IntegerNumber;
                count._inputField.lineType = TMP_InputField.LineType.SingleLine;
            }

            PanelElementDescriptor translationGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            translationGroup.SetTitle("字幕");

            PanelTextField language = TextField(translationGroup.ContentParent, "翻訳先言語", host.Config.TargetLanguage, 80);
            PanelTextField url = TextField(translationGroup.ContentParent, "翻訳API ベースURL", host.Config.TranslationBaseUrl, 256);
            url.Descriptor.SetTooltip("例: https://example.com/v1");
            PanelTextField model = TextField(translationGroup.ContentParent, "翻訳モデル名", host.Config.TranslationModel, 256);

            PanelElementDescriptor voiceGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            voiceGroup.SetTitle("音声翻訳");

            PanelDropdown voiceLanguage = PanelDropdown.CreateNewEntry(voiceGroup.ContentParent);
            voiceLanguage.Descriptor.SetTitle("翻訳先言語");
            voiceLanguage.AssignEntries(new List<string>(QwenProtocol.VoiceLanguages), new List<string>(QwenProtocol.VoiceLanguageNames));
            voiceLanguage.SetValueWithoutNotify(QwenProtocol.IsVoiceLanguage(host.Config.VoiceLanguage) ? host.Config.VoiceLanguage : "ja");

            PanelTextField qwenUrl = TextField(voiceGroup.ContentParent, "Qwen Realtime URL", host.Config.QwenRealtimeUrl, 256);
            qwenUrl.Descriptor.SetTooltip("初期値は国際リージョンです。中国リージョンは wss://dashscope.aliyuncs.com/api-ws/v1/realtime");

            PanelSlider originalGain = PanelSlider.CreateNew(PanelSlider.SliderStyles.Entry, voiceGroup.ContentParent);
            originalGain.SetSliderSettings(new PanelSlider.SliderSettings("元の声の音量", "", 0, 1, false, 0, ValueDisplayMode.Percentage));
            originalGain.Descriptor.SetTooltip("翻訳対象の元の声をどれだけ残すか。翻訳音声は通常音量です。");
            originalGain.SetValueWithoutNotify(host.Config.OriginalVoiceGain);

            PanelElementDescriptor keysGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            keysGroup.SetTitle("APIキー");
            keysGroup.SetDescription("空欄で保存済みのキーを使います。保存後は欄が空になります。");

            PanelPasswordField speechKey = PasswordField(keysGroup.ContentParent, "xAI APIキー");
            PanelPasswordField translationKey = PasswordField(keysGroup.ContentParent, "翻訳APIキー");
            PanelPasswordField dashscopeKey = PasswordField(keysGroup.ContentParent, "DashScope APIキー");

            PanelButton apply = PanelButton.CreateNew(keysGroup.ContentParent);
            apply.Descriptor.SetTitle("保存して適用 / 再接続");
            apply.OnClicked += () =>
            {
                try
                {
                    if (!int.TryParse(count.Value, out int maximum))
                        throw new ArgumentException("最大話者数には整数を入力してください。");
                    var config = new TranslatorConfiguration
                    {
                        Enabled = enabled.Value,
                        Mode = mode.Value == "voice" ? TranslationMode.Voice : TranslationMode.Captions,
                        TargetLanguage = language.Value.Trim(),
                        TranslationBaseUrl = url.Value.Trim(),
                        TranslationModel = model.Value.Trim(),
                        VoiceLanguage = string.IsNullOrWhiteSpace(voiceLanguage.Value) ? "ja" : voiceLanguage.Value.Trim(),
                        QwenRealtimeUrl = qwenUrl.Value.Trim(),
                        OriginalVoiceGain = originalGain.Value,
                        MaxSpeakers = maximum
                    };
                    host.Apply(config, speechKey.Password, translationKey.Password, dashscopeKey.Password);
                    speechKey.SetPassword("");
                    translationKey.SetPassword("");
                    dashscopeKey.SetPassword("");
                }
                catch (ArgumentException ex) { host.ShowError(ex.Message); }
                catch (Exception) { host.ShowError("設定またはAPIキーを保存できません。入力内容と保存先を確認してください。"); }
            };

            PanelButton clear = PanelButton.CreateNew(keysGroup.ContentParent);
            clear.Descriptor.SetTitle("停止して保存済みAPIキーを削除");
            clear.OnClicked += () =>
            {
                host.ClearKeys();
                enabled.SetValueWithoutNotify(false);
                speechKey.SetPassword("");
                translationKey.SetPassword("");
                dashscopeKey.SetPassword("");
            };

            PanelElementDescriptor statusGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            statusGroup.SetTitle("接続状態");
            statusGroup.SetDescription(host.Status);
            tab.gameObject.AddComponent<TranslatorPanelStatus>().Label = statusGroup;

            descriptor.ForceRebuild();
            return tab;
        }

        private static PanelTextField TextField(Component parent, string title, string value, int characterLimit)
        {
            PanelTextField field = PanelTextField.CreateNewEntry(parent);
            field.Descriptor.SetTitle(title);
            if (field._inputField != null)
            {
                field._inputField.contentType = TMP_InputField.ContentType.Standard;
                field._inputField.lineType = TMP_InputField.LineType.SingleLine;
                field._inputField.characterLimit = characterLimit;
            }
            field.SetValueWithoutNotify(value);
            return field;
        }

        private static PanelPasswordField PasswordField(Component parent, string title)
        {
            PanelPasswordField field = PanelPasswordField.CreateNewEntry(parent);
            field.Descriptor.SetTitle(title);
            if (field._inputField != null) field._inputField.characterLimit = 512;
            field.SetPassword("");
            return field;
        }
    }
}
