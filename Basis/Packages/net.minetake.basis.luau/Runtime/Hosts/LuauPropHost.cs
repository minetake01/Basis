using Luau;
using UnityEngine;

namespace Minetake.Basis.Luau
{
    [DisallowMultipleComponent]
    public sealed class LuauPropHost : LuauHostBase
    {
        protected override LuauHostKind DefaultHostKind => LuauHostKind.Prop;
        protected override void RegisterServiceBindings(LuauState state)
        {
            Services.BasisLuauOsc.Install(state, this);
            Services.BasisLuauNetworkBridge.Install(state, this);
            Services.BasisLuauInstantiateService.Install(state, this);
            Services.BasisLuauUtil.Install(state, this);
        }
    }
}
