using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.Device_Management.Devices;
using UnityEngine;

namespace Basis.TransparentMirror
{
    public class TransparentMirrorButton : BasisInteractableObject
    {
        public delegate void ClickEvent();

        public ClickEvent ButtonDown { get; set; }
        public ClickEvent ButtonUp { get; set; }

        [Header("Button Settings")]
        public bool isEnabled = true;
        public string PropertyName = "_BaseColor";
        public Color Color = Color.white;
        public Color HoverColor = Color.white;
        public Color InteractColor = Color.white;
        public Color DisabledColor = Color.white;

        [Header("References")]
        public Collider ColliderRef;
        public MeshRenderer RendererRef;

        private BasisInputWrapper _inputSource;

        private BasisInputWrapper InputSource
        {
            get => _inputSource;
            set
            {
                if (value.Source != null)
                {
                    Inputs = new BasisInputSources(0);
                    Inputs.SetInputByRole(value.Source, value.GetState());
                }
                else
                {
                    Inputs = new BasisInputSources(0);
                }

                _inputSource = value;
            }
        }

        private void Start()
        {
            InputSource = default;
            if (ColliderRef == null)
            {
                TryGetComponent(out ColliderRef);
            }

            if (RendererRef == null)
            {
                TryGetComponent(out RendererRef);
            }

            SetColor(isEnabled ? Color : DisabledColor);
        }

        public override bool CanHover(BasisInput input)
        {
            return InputSource.GetState() == BasisInteractInputState.NotAdded &&
                   IsWithinRange(input.transform.position, InteractRange) &&
                   isEnabled;
        }

        public override bool CanInteract(BasisInput input)
        {
            if (!InputSource.IsInput(input))
            {
                return false;
            }

            if (InputSource.GetState() == BasisInteractInputState.Interacting)
            {
                return false;
            }

            return IsWithinRange(input.transform.position, InteractRange) && isEnabled;
        }

        public override void OnHoverStart(BasisInput input)
        {
            if (!BasisInputWrapper.TryNewTracking(input, BasisInteractInputState.Hovering, out BasisInputWrapper wrapper))
            {
                return;
            }

            InputSource = wrapper;
            SetColor(HoverColor);
            base.OnHoverStart(input);
        }

        public override void OnHoverEnd(BasisInput input, bool willInteract)
        {
            if (InputSource.IsInput(input))
            {
                if (!willInteract)
                {
                    _ = BasisInputWrapper.TryNewTracking(null, BasisInteractInputState.NotAdded, out BasisInputWrapper wrapper);
                    InputSource = wrapper;
                    SetColor(Color);
                }

                base.OnHoverEnd(input, willInteract);
            }
        }

        public override void OnInteractStart(BasisInput input)
        {
            if (InputSource.IsInput(input) && InputSource.GetState() == BasisInteractInputState.Hovering)
            {
                SetColor(InteractColor);

                var newSource = InputSource;
                _ = newSource.TrySetState(BasisInteractInputState.Interacting);
                InputSource = newSource;

                ButtonDown?.Invoke();
                base.OnInteractStart(input);
            }
        }

        public override void OnInteractEnd(BasisInput input)
        {
            if (InputSource.IsInput(input))
            {
                SetColor(Color);
                _ = BasisInputWrapper.TryNewTracking(null, BasisInteractInputState.NotAdded, out BasisInputWrapper wrapper);
                InputSource = wrapper;

                ButtonUp?.Invoke();
                base.OnInteractEnd(input);
            }
        }

        public override bool IsInteractingWith(BasisInput input)
        {
            return InputSource.IsInput(input) &&
                   InputSource.GetState() == BasisInteractInputState.Interacting;
        }

        public override bool IsHoveredBy(BasisInput input)
        {
            return InputSource.IsInput(input) &&
                   InputSource.GetState() == BasisInteractInputState.Hovering;
        }

        public void SetBaseColor(Color color)
        {
            Color = color;
            if (InputSource.GetState() == BasisInteractInputState.NotAdded)
            {
                SetColor(color);
            }
        }

        private void SetColor(Color color)
        {
            if (RendererRef != null && RendererRef.material != null)
            {
                RendererRef.material.SetColor(Shader.PropertyToID(PropertyName), color);
            }
        }

        private bool _triggerCleanup;

        public override void InputUpdate()
        {
            if (!isEnabled)
            {
                if (_triggerCleanup)
                {
                    _triggerCleanup = false;
                    if (InputSource.GetState() != BasisInteractInputState.NotAdded)
                    {
                        if (IsHoveredBy(InputSource.Source))
                        {
                            OnHoverEnd(InputSource.Source, false);
                        }

                        if (IsInteractingWith(InputSource.Source))
                        {
                            OnInteractEnd(InputSource.Source);
                        }
                    }

                    SetColor(DisabledColor);
                }
            }
            else
            {
                _triggerCleanup = true;
            }
        }
    }
}
