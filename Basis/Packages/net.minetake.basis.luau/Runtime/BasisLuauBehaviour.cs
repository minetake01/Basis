using Luau.Unity;
using UnityEngine;

namespace Minetake.Basis.Luau
{
    /// <summary>
    /// Editor authoring marker. Removed at export by <see cref="Editor.BasisLuauBuildHook"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BasisLuauBehaviour : MonoBehaviour
    {
        [SerializeField] LuauHostKind hostKind = LuauHostKind.Prop;
        [SerializeField] LuauAsset script;
        [SerializeField] UnityEngine.Object[] handleSlots = System.Array.Empty<UnityEngine.Object>();

        public LuauHostKind HostKind => hostKind;
        public LuauAsset Script => script;
        public UnityEngine.Object[] HandleSlots => handleSlots;
    }
}
