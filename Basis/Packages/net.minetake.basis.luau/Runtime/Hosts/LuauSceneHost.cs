using Luau;
using UnityEngine;

namespace Minetake.Basis.Luau
{
    [DisallowMultipleComponent]
    public sealed class LuauSceneHost : LuauHostBase
    {
        protected override LuauHostKind DefaultHostKind => LuauHostKind.Scene;
        protected override void RegisterServiceBindings(LuauState state)
        {
            Services.BasisLuauOsc.Install(state, this);
            Services.BasisLuauNetworkBridge.Install(state, this);
            Services.BasisLuauInstantiateService.Install(state, this);
            Services.BasisLuauUtil.Install(state, this);
        }
    }
}
