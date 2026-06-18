using Luau;
using Minetake.Basis.Luau.Bindings;
using UnityEngine;

namespace Minetake.Basis.Luau.Bindings
{
    [LuauLibrary("basis_time")]
    public partial class TimeBindings
    {
        [LuauMember("deltaTime")]
        public static double DeltaTime() => Time.deltaTime;

        [LuauMember("fixedDeltaTime")]
        public static double FixedDeltaTime() => Time.fixedDeltaTime;

        [LuauMember("time")]
        public static double TimeSinceLoad() => Time.time;

        [LuauMember("unscaledDeltaTime")]
        public static double UnscaledDeltaTime() => Time.unscaledDeltaTime;
    }
}
