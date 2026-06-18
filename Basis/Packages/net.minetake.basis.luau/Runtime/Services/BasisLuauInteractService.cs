using System;
using Luau;
using Minetake.Basis.Luau.Bindings;
using UnityEngine;

namespace Minetake.Basis.Luau.Services
{
    [LuauLibrary("basis_interact")]
    public partial class BasisLuauInteractService
    {
        public static void Install(LuauState state, LuauHostBase host)
        {
            state?.OpenLibrary<BasisLuauInteractService>();
        }

        [LuauMember("triggerButtonDown")]
        public static void TriggerButtonDown(double handleRaw)
        {
            if (TryGet(handleRaw, out var shim))
            {
                shim.TriggerButtonDown();
            }
        }

        [LuauMember("triggerButtonUp")]
        public static void TriggerButtonUp(double handleRaw)
        {
            if (TryGet(handleRaw, out var shim))
            {
                shim.TriggerButtonUp();
            }
        }

        [LuauMember("setEnabled")]
        public static void SetEnabled(double handleRaw, bool enabled)
        {
            if (TryGet(handleRaw, out var shim))
            {
                shim.isEnabled = enabled;
            }
        }

        [LuauMember("onButtonDown")]
        public static void OnButtonDown(double handleRaw, LuauFunction callback)
        {
            if (!TryGet(handleRaw, out var shim) || callback == null)
            {
                return;
            }

            var host = LuauBindingContext.Host;
            var proxy = LuauBindingContext.Proxy;
            shim.ButtonDown += () => host?.InvokeCallback(proxy, callback);
        }

        [LuauMember("onButtonUp")]
        public static void OnButtonUp(double handleRaw, LuauFunction callback)
        {
            if (!TryGet(handleRaw, out var shim) || callback == null)
            {
                return;
            }

            var host = LuauBindingContext.Host;
            var proxy = LuauBindingContext.Proxy;
            shim.ButtonUp += () => host?.InvokeCallback(proxy, callback);
        }

        static bool TryGet(double handleRaw, out BasisLuauInteractableShim shim)
        {
            shim = null;
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

            shim = obj as BasisLuauInteractableShim;
            return shim != null;
        }
    }
}
