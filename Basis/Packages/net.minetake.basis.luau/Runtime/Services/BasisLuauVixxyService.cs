using HVR.Vixxy;
using Luau;
using Minetake.Basis.Luau.Bindings;
using UnityEngine;

namespace Minetake.Basis.Luau.Services
{
    [LuauLibrary("basis_vixxy")]
    public partial class BasisLuauVixxyService
    {
        public static void Install(LuauState state, LuauHostBase host)
        {
            state?.OpenLibrary<BasisLuauVixxyService>();
        }

        [LuauMember("getValue")]
        public static double GetValue(double handleRaw)
        {
            if (!TryGet(handleRaw, out var item))
            {
                return 0;
            }

            return item.GetValue();
        }

        [LuauMember("applyValue")]
        public static void ApplyValue(double handleRaw, double value)
        {
            if (TryGet(handleRaw, out var item))
            {
                item.ApplyValue((float)value);
            }
        }

        static bool TryGet(double handleRaw, out HVRVixxyMenuItem item)
        {
            item = null;
            var host = LuauBindingContext.Host;
            if (host == null)
            {
                return false;
            }

            var handle = Registry.LuauObjectHandle.FromRaw((ulong)handleRaw);
            if (!host.Registry.TryResolve(handle, out UnityEngine.Object obj))
            {
                return false;
            }

            item = obj as HVRVixxyMenuItem;
            return item != null;
        }
    }
}
