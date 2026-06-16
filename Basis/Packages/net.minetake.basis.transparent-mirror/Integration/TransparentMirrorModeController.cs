using UnityEngine;

namespace Basis.TransparentMirror
{
    public class TransparentMirrorModeController : MonoBehaviour
    {
        [Header("References")]
        public TransparentMirror Mirror;
        public TransparentMirrorButton ModeButton;

        [Header("Mode Colors")]
        public Color OffColor = new Color(0.45f, 0.45f, 0.45f, 1f);
        public Color FullColor = new Color(0.2f, 0.45f, 0.95f, 1f);
        public Color TransparentColor = new Color(0.2f, 0.85f, 0.35f, 1f);

        private void Awake()
        {
            if (Mirror == null)
            {
                Mirror = GetComponentInChildren<TransparentMirror>(true);
            }
        }

        private void Start()
        {
            if (Mirror != null)
            {
                Mirror.OnModeChanged += OnMirrorModeChanged;
                Mirror.SetMode(TransparentMirrorMode.Off);
            }

            if (ModeButton != null)
            {
                ModeButton.ButtonDown += OnModeButtonPressed;
            }

            RefreshButtonColor();
        }

        private void OnDestroy()
        {
            if (Mirror != null)
            {
                Mirror.OnModeChanged -= OnMirrorModeChanged;
            }

            if (ModeButton != null)
            {
                ModeButton.ButtonDown -= OnModeButtonPressed;
            }
        }

        private void OnModeButtonPressed()
        {
            Mirror?.CycleMode();
        }

        private void OnMirrorModeChanged(TransparentMirrorMode mode)
        {
            RefreshButtonColor();
        }

        private void RefreshButtonColor()
        {
            if (ModeButton == null || Mirror == null)
            {
                return;
            }

            Color color = Mirror.CurrentMode switch
            {
                TransparentMirrorMode.Full => FullColor,
                TransparentMirrorMode.Transparent => TransparentColor,
                _ => OffColor,
            };

            ModeButton.Color = color;
            ModeButton.HoverColor = Color.Lerp(color, Color.white, 0.25f);
            ModeButton.SetBaseColor(color);
        }
    }
}
