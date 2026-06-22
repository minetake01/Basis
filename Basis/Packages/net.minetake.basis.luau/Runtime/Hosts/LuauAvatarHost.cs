using UnityEngine;

namespace Minetake.Basis.Luau
{
    [DisallowMultipleComponent]
    public sealed class LuauAvatarHost : LuauHostBase
    {
        protected override LuauHostKind DefaultHostKind => LuauHostKind.Avatar;
    }
}
