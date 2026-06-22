using UnityEngine;

namespace Minetake.Basis.Luau
{
    [DisallowMultipleComponent]
    public sealed class LuauSceneHost : LuauHostBase
    {
        protected override LuauHostKind DefaultHostKind => LuauHostKind.Scene;
    }
}
