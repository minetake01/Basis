using Luau;
using UnityEngine;

namespace Minetake.Basis.Luau
{
    [DisallowMultipleComponent]
    public sealed class LuauPropHost : LuauHostBase
    {
        protected override LuauHostKind DefaultHostKind => LuauHostKind.Prop;
    }
}
