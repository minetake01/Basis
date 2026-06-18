using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace Minetake.Basis.Luau.Services
{
    public static class BasisLuauSafeUtil
    {
        public static void AddEventTrigger(Component target, EventTriggerType eventType, UnityAction<BaseEventData> callback)
        {
            if (target == null || callback == null)
            {
                return;
            }

            if (!target.TryGetComponent(out EventTrigger eventTrigger))
            {
                return;
            }

            var entry = new EventTrigger.Entry
            {
                eventID = eventType,
                callback = new EventTrigger.TriggerEvent(),
            };
            entry.callback.AddListener(callback);
            eventTrigger.triggers ??= new List<EventTrigger.Entry>();
            eventTrigger.triggers.Add(entry);
        }

        public static BasisNetworkShim MakeNetworkable(object o)
        {
            if (o is not MonoBehaviour behaviour)
            {
                Debug.LogError($"Object {o} is not a MonoBehaviour and cannot be made networkable.");
                return null;
            }

            return MakeNetworkable(behaviour);
        }

        public static BasisNetworkShim MakeNetworkable(MonoBehaviour mb)
        {
            if (mb == null)
            {
                return null;
            }

            if (mb.TryGetComponent(out BasisNetworkShim existing))
            {
                return existing;
            }

            return mb.gameObject.AddComponent<BasisNetworkShim>();
        }

        public static BasisLuauInteractableShim MakeInteractable(object o)
        {
            GameObject go = o switch
            {
                MonoBehaviour behaviour => behaviour.gameObject,
                GameObject gameObject => gameObject,
                Component component => component.gameObject,
                _ => null,
            };

            if (go == null)
            {
                return null;
            }

            if (go.TryGetComponent(out BasisLuauInteractableShim existing))
            {
                return existing;
            }

            return go.AddComponent<BasisLuauInteractableShim>();
        }
    }
}
