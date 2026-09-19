using System;
using Basis.BasisUI;
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
            aboutGroup.SetDescription("周囲の音声をxAIへ、文字起こしを翻訳APIへ、ご自身のキーで送信します。字幕は自分だけに表示されます。");

            PanelToggle enabled = PanelToggle.CreateNewEntry(aboutGroup.ContentParent);
            enabled.Descriptor.SetTitle("有効にする");
            enabled.Descriptor.SetTooltip("保存して適用を押すまで反映されません。");
            enabled.SetValueWithoutNotify(host.Config.Enabled);

            PanelElementDescriptor translationGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            translationGroup.SetTitle("翻訳");

            PanelTextField language = TextField(translationGroup.ContentParent, "翻訳先言語", host.Config.TargetLanguage, 80);
            PanelTextField url = TextField(translationGroup.ContentParent, "翻訳API ベースURL", host.Config.TranslationBaseUrl, 256);
            url.Descriptor.SetTooltip("例: https://example.com/v1");
            PanelTextField model = TextField(translationGroup.ContentParent, "翻訳モデル名", host.Config.TranslationModel, 256);
            PanelTextField count = TextField(translationGroup.ContentParent, "最大話者数", host.Config.MaxSpeakers.ToString(), 2);
            count.Descriptor.SetTooltip("1〜64。8チャンネル単位で接続します。");
            if (count._inputField != null)
            {
                count._inputField.contentType = TMP_InputField.ContentType.IntegerNumber;
                count._inputField.lineType = TMP_InputField.LineType.SingleLine;
            }

            PanelElementDescriptor keysGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            keysGroup.SetTitle("APIキー");
            keysGroup.SetDescription("空欄で保存済みのキーを使います。保存後は欄が空になります。");

            PanelPasswordField speechKey = PasswordField(keysGroup.ContentParent, "xAI APIキー");
            PanelPasswordField translationKey = PasswordField(keysGroup.ContentParent, "翻訳APIキー");

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
                        TargetLanguage = language.Value.Trim(),
                        TranslationBaseUrl = url.Value.Trim(),
                        TranslationModel = model.Value.Trim(),
                        MaxSpeakers = maximum
                    };
                    host.Apply(config, speechKey.Password, translationKey.Password);
                    speechKey.SetPassword("");
                    translationKey.SetPassword("");
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
