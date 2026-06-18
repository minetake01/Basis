using System;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk;
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
        public static void Log(string message)
        {
            Debug.Log($"[Luau] {message}");
        }

        [LuauMember("addEventTrigger")]
        public static void AddEventTrigger(double handleRaw, int eventType, LuauFunction callback)
        {
            var host = LuauBindingContext.Host;
            if (host == null || callback == null)
            {
                return;
            }

            var handle = Registry.LuauObjectHandle.FromRaw((ulong)handleRaw);
            if (!host.Registry.TryResolve(handle, out UnityEngine.Object obj))
            {
                return;
            }

            if (obj is not Component component)
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
            entry.callback.AddListener(_ =>
                callback.InvokeAsync(Array.Empty<LuauValue>()).AsTask().GetAwaiter().GetResult());
            trigger.triggers ??= new System.Collections.Generic.List<EventTrigger.Entry>();
            trigger.triggers.Add(entry);
        }
    }
}
