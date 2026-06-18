using System;
using Luau;
using Minetake.Basis.Luau.Bindings;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Minetake.Basis.Luau.Services
{
    [LuauLibrary("basis_util")]
    public partial class BasisLuauUtil
    {
        public static void Install(LuauState state, LuauHostBase host)
        {
            state?.OpenLibrary<BasisLuauUtil>();
        }

        [LuauMember("log")]
        public static void Log(string message) => BasisLuauDebug.Log(message);

        [LuauMember("warn")]
        public static void Warn(string message) => BasisLuauDebug.LogWarning(message);

        [LuauMember("error")]
        public static void Error(string message) => BasisLuauDebug.LogError(message);

        [LuauMember("makeNetworkable")]
        public static double MakeNetworkable(double handleRaw) =>
            BasisLuauNetworkBridge.MakeNetworkable(handleRaw);

        [LuauMember("makeInteractable")]
        public static double MakeInteractable(double handleRaw)
        {
            var host = LuauBindingContext.Host;
            var handle = Registry.LuauObjectHandle.FromRaw((ulong)handleRaw);
            if (host == null || !host.Registry.TryResolve(handle, out UnityEngine.Object obj))
            {
                return 0;
            }

            var shim = BasisLuauSafeUtil.MakeInteractable(obj);
            return shim != null ? host.RegisterObject(shim).ToRaw() : 0;
        }

        [LuauMember("addEventTrigger")]
        public static void AddEventTrigger(double handleRaw, int eventType, LuauFunction callback)
        {
            var host = LuauBindingContext.Host;
            var proxy = LuauBindingContext.Proxy;
            if (host == null || proxy == null || callback == null)
            {
                return;
            }

            var handle = Registry.LuauObjectHandle.FromRaw((ulong)handleRaw);
            if (!host.Registry.TryResolve(handle, out UnityEngine.Object obj) || obj is not Component component)
            {
                return;
            }

            if (!component.TryGetComponent(out EventTrigger trigger))
            {
                return;
            }

            var entry = new EventTrigger.Entry
            {
                eventID = (EventTriggerType)eventType,
                callback = new EventTrigger.TriggerEvent(),
            };
            entry.callback.AddListener(_ => host.InvokeCallback(proxy, callback));
            trigger.triggers ??= new System.Collections.Generic.List<EventTrigger.Entry>();
            trigger.triggers.Add(entry);
        }
    }
}
