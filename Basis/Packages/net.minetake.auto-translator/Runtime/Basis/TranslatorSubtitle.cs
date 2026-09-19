using Basis.Scripts.UI.NamePlate;
using TMPro;
using UnityEngine;

namespace Net.Minetake.AutoTranslator.BasisIntegration
{
    public sealed class TranslatorSubtitle : MonoBehaviour
    {
        private TextMeshPro original, translated;
        private BasisRemoteNamePlate plate;
        private string originalText, translationText;
        public static TranslatorSubtitle Create(Transform parent)
        {
            var go = new GameObject("Local translation subtitles"); go.transform.SetParent(parent, false); go.layer = parent.gameObject.layer;
            var view = go.AddComponent<TranslatorSubtitle>(); view.plate = parent.GetComponent<BasisRemoteNamePlate>();
            view.original = view.CreateText("Original", 16, new Color(0.8f, 0.85f, 0.9f), 12);
            view.translated = view.CreateText("Translation", 24, Color.white, 24);
            return view;
        }
        private TextMeshPro CreateText(string name, float size, Color color, float height)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false); go.layer = gameObject.layer;
            go.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var text = go.AddComponent<TextMeshPro>();
            if (plate != null && plate.LoadingText != null) text.font = plate.LoadingText.font;
            text.richText = false; text.fontSize = size; text.color = color;
            text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.Normal;
            text.enableAutoSizing = true; text.fontSizeMin = size * 0.65f; text.fontSizeMax = size;
            text.overflowMode = TextOverflowModes.Ellipsis; text.rectTransform.sizeDelta = new Vector2(70, height);
            return text;
        }
        public void Render(CaptionState state)
        {
            bool visible = TranslationClock.Now < state.ExpiresAt && state.Original.Length != 0;
            original.enabled = visible; translated.enabled = visible;
            if (!visible) return;
            string source = state.Final ? state.Original : "… " + state.Original;
            string target = state.Translation.Length == 0 ? "翻訳中…" : state.Translation;
            if (source != originalText) { originalText = source; original.text = source; }
            if (target != translationText) { translationText = target; translated.text = target; }
            float bottom = 10;
            if (plate != null && plate.ChatText != null && plate.ChatText.gameObject.activeSelf)
                bottom = plate.ChatText.transform.localPosition.y + plate.ChatText.rectTransform.rect.height * 0.5f + 3;
            original.transform.localPosition = new Vector3(0, bottom + 6, 0.05f);
            translated.transform.localPosition = new Vector3(0, bottom + 24, 0.05f);
        }
    }
}
