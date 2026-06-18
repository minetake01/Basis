using Luau;
using UnityEngine;

namespace Minetake.Basis.Luau
{
    [DisallowMultipleComponent]
    public sealed class LuauAvatarHost : LuauHostBase
    {
        protected override LuauHostKind DefaultHostKind => LuauHostKind.Avatar;

        protected override void RegisterServiceBindings(LuauState state)
        {
            Services.BasisLuauOsc.Install(state, this);
            Services.BasisLuauNetworkBridge.Install(state, this);
            Services.BasisLuauInstantiateService.Install(state, this);
            Services.BasisLuauUtil.Install(state, this);
            Services.BasisLuauAvatarService.Install(state, this);
            Services.BasisLuauVixxyService.Install(state, this);
        }
    }
}
