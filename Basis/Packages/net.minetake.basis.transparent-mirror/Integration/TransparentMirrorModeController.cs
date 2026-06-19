using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.Device_Management.Devices;
using UnityEngine;

namespace Basis.TransparentMirror
{
    [Cilboxable]
    public class TransparentMirrorModeController : MonoBehaviour
    {
        public GameObject ButtonObject;
        public GameObject MirrorFull;
        public GameObject MirrorTransparent;
        public MeshRenderer ButtonRenderer;
        public Color OffColor = new Color(0.45f, 0.45f, 0.45f, 1f);
        public Color FullColor = new Color(0.2f, 0.45f, 0.95f, 1f);
        public Color TransparentColor = new Color(0.2f, 0.85f, 0.35f, 1f);
        public string ColorPropertyName = "_BaseColor";

        private int _mode;

        private void Start()
        {
            ApplyMode();

            if (ButtonObject == null)
            {
                return;
            }

            BasisInteractableObject interactable = ButtonObject.GetComponent<BasisInteractableObject>();
            if (interactable != null)
            {
                interactable.OnInteractStartEvent.AddListener(CycleMode);
            }
        }

        public void CycleMode(BasisInput input)
        {
            _mode = (_mode + 1) % 3;
            ApplyMode();
        }

        private void ApplyMode()
        {
            if (MirrorFull != null)
            {
                MirrorFull.SetActive(_mode == 1);
            }

            if (MirrorTransparent != null)
            {
                MirrorTransparent.SetActive(_mode == 2);
            }

            UpdateButtonColor();
        }

        private void UpdateButtonColor()
        {
            if (ButtonRenderer == null)
            {
                return;
            }

            Color color = _mode switch
            {
                1 => FullColor,
                2 => TransparentColor,
                _ => OffColor,
            };

            Material material = ButtonRenderer.material;
            if (material != null)
            {
                material.SetColor(Shader.PropertyToID(ColorPropertyName), color);
            }
        }
    }
}
