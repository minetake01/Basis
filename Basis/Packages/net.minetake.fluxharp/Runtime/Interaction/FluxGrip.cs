using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Net.Minetake.FluxHarp
{
    /// <summary>Where a hand or a pointer is while it holds something on the instrument.</summary>
    public struct FluxPointer
    {
        public Vector3 Origin;
        public Vector3 Direction;

        /// <summary>Grabbed with the hand itself rather than pointed at from a distance.</summary>
        public bool Direct;
        public Vector3 Hand;

        /// <summary>The point being held: the hand when direct, otherwise along the ray at the grab distance.</summary>
        public Vector3 At(float distance) => Direct ? Hand : Origin + Direction * distance;
    }

    /// <summary>What a grip reports to. The board decides what a press, drag and release mean.</summary>
    public interface IFluxGripTarget
    {
        void Press(FluxGrip grip, in FluxPointer pointer);
        void Hold(FluxGrip grip, in FluxPointer pointer);
        void Release(FluxGrip grip, in FluxPointer pointer);
        void Hover(FluxGrip grip, bool hovered);
    }

    /// <summary>
    /// Makes a collider on the instrument something a hand or pointer can take hold of: press, hold while
    /// moving, let go. Grip or trigger in VR, mouse on desktop.
    /// </summary>
    public sealed class FluxGrip : BasisInteractableObject
    {
        public IFluxGripTarget Target;
        public int Role;
        public ushort Node;
        public int Index;

        private FluxPointer _last;

        public static FluxGrip Add(GameObject go, IFluxGripTarget target, int role, ushort node = 0, int index = 0)
        {
            FluxGrip grip = go.AddComponent<FluxGrip>();
            grip.Target = target;
            grip.Role = role;
            grip.Node = node;
            grip.Index = index;
            grip.InteractRange = 8f;
            grip.GrabRadius = 0.06f;
            grip.AutoHold = BasisAutoHold.None;
            return grip;
        }

        public override void Awake()
        {
            base.Awake();
            RequiresUpdateLoop = false;
        }

        public override bool IsInteractTriggered(BasisInput input)
        {
            BasisInputState state = input.CurrentInputState;
            return state.GripButton || state.Trigger >= BasisTriggerPressure.SnapInteractableThreshold;
        }

        public override bool CanHover(BasisInput input) => Usable(input, BasisInteractInputState.Ignored);

        public override bool CanInteract(BasisInput input) => Usable(input, BasisInteractInputState.Hovering);

        private bool Usable(BasisInput input, BasisInteractInputState required)
        {
            return InteractableEnabled
                && !Inputs.AnyInteracting()
                && !PointerClaimedByUI(input)
                && Inputs.IsInputAdded(input)
                && input.TryGetRole(out BasisBoneTrackedRole role)
                && Inputs.TryGetByRole(role, out BasisInputWrapper found)
                && found.GetState() == required
                && IsWithinRange(found.BoneControl.OutgoingWorldData.position, InteractRange);
        }

        public override bool IsHoveredBy(BasisInput input)
        {
            BasisInputWrapper? found = Inputs.FindExcludeExtras(input);
            return found.HasValue && found.Value.GetState() == BasisInteractInputState.Hovering;
        }

        public override bool IsInteractingWith(BasisInput input)
        {
            BasisInputWrapper? found = Inputs.FindExcludeExtras(input);
            return found.HasValue && found.Value.GetState() == BasisInteractInputState.Interacting;
        }

        public override void OnHoverStart(BasisInput input)
        {
            if (!input.TryGetRole(out BasisBoneTrackedRole role)) return;
            Inputs.ChangeStateByRole(role, BasisInteractInputState.Hovering);
            Target?.Hover(this, true);
            base.OnHoverStart(input);
        }

        public override void OnHoverEnd(BasisInput input, bool willInteract)
        {
            if (!input.TryGetRole(out BasisBoneTrackedRole role)) return;
            if (!willInteract) Inputs.ChangeStateByRole(role, BasisInteractInputState.Ignored);
            Target?.Hover(this, false);
            base.OnHoverEnd(input, willInteract);
        }

        public override void OnInteractStart(BasisInput input)
        {
            if (!InteractionTimerValidation()) return;
            if (!input.TryGetRole(out BasisBoneTrackedRole role)) return;
            if (!Inputs.TryGetByRole(role, out BasisInputWrapper wrapper)) return;
            if (wrapper.GetState() != BasisInteractInputState.Hovering) return;

            Inputs.ChangeStateByRole(role, BasisInteractInputState.Interacting);
            RequiresUpdateLoop = true;
            _last = PointerOf(wrapper);
            Target?.Press(this, _last);
            base.OnInteractStart(input);
        }

        public override void OnInteractEnd(BasisInput input)
        {
            if (!input.TryGetRole(out BasisBoneTrackedRole role)) return;
            if (!Inputs.TryGetByRole(role, out BasisInputWrapper wrapper)) return;
            if (wrapper.GetState() != BasisInteractInputState.Interacting) return;

            Inputs.ChangeStateByRole(role, BasisInteractInputState.Ignored);
            RequiresUpdateLoop = false;
            Target?.Release(this, PointerOf(wrapper));
            base.OnInteractEnd(input);
        }

        public override void InputUpdate()
        {
            if (!TryGetInteracting(out BasisInputWrapper wrapper)) return;
            _last = PointerOf(wrapper);
            Target?.Hold(this, _last);
        }

        private bool TryGetInteracting(out BasisInputWrapper wrapper)
        {
            if (Inputs.desktopCenterEye.GetState() == BasisInteractInputState.Interacting) { wrapper = Inputs.desktopCenterEye; return true; }
            if (Inputs.rightHand.GetState() == BasisInteractInputState.Interacting) { wrapper = Inputs.rightHand; return true; }
            if (Inputs.leftHand.GetState() == BasisInteractInputState.Interacting) { wrapper = Inputs.leftHand; return true; }
            wrapper = default;
            return false;
        }

        private FluxPointer PointerOf(BasisInputWrapper wrapper)
        {
            Vector3 hand = wrapper.BoneControl.OutgoingWorldData.position;
            bool isHand = wrapper.Role == BasisBoneTrackedRole.LeftHand || wrapper.Role == BasisBoneTrackedRole.RightHand;
            float reach = GrabRadius * 2.5f;
            FluxPointer p = new FluxPointer
            {
                Hand = hand,
                Direct = isHand && (GetClosestPoint(hand) - hand).sqrMagnitude <= reach * reach,
            };
            BasisInput source = wrapper.Source;
            if (source != null)
            {
                p.Origin = source.RaycastCoord.position;
                p.Direction = (source.RaycastCoord.rotation * Vector3.forward).normalized;
            }
            else
            {
                p.Origin = hand;
                p.Direction = Vector3.forward;
            }
            return p;
        }
    }
}
