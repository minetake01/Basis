using System;
using System.Collections.Generic;
using Luau.Unity;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Minetake.Basis.Luau.Runtime
{
    public static class LuauBridgePump
    {
        static readonly List<LuauHostRuntimeBridge> Bridges = new();
        static bool _registered;

        public static void Register(LuauHostRuntimeBridge bridge)
        {
            if (bridge == null || Bridges.Contains(bridge))
            {
                return;
            }

            Bridges.Add(bridge);
            EnsurePlayerLoop();
        }

        public static void Unregister(LuauHostRuntimeBridge bridge)
        {
            if (bridge != null)
            {
                Bridges.Remove(bridge);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Bridges.Clear();

        static void EnsurePlayerLoop()
        {
            if (_registered)
            {
                return;
            }

            _registered = true;
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(EarlyUpdate))
                {
                    continue;
                }

                PlayerLoopSystem early = loop.subSystemList[i];
                var list = new List<PlayerLoopSystem>(early.subSystemList ?? Array.Empty<PlayerLoopSystem>());
                list.Add(new PlayerLoopSystem
                {
                    type = typeof(LuauBridgePump),
                    updateDelegate = EarlyUpdateTick,
                });
                early.subSystemList = list.ToArray();
                loop.subSystemList[i] = early;
                break;
            }

            PlayerLoop.SetPlayerLoop(loop);

            loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(FixedUpdate))
                {
                    continue;
                }

                PlayerLoopSystem fixedUpdate = loop.subSystemList[i];
                var list = new List<PlayerLoopSystem>(fixedUpdate.subSystemList ?? Array.Empty<PlayerLoopSystem>());
                list.Add(new PlayerLoopSystem
                {
                    type = typeof(LuauBridgePump),
                    updateDelegate = FixedUpdateTick,
                });
                fixedUpdate.subSystemList = list.ToArray();
                loop.subSystemList[i] = fixedUpdate;
                break;
            }

            PlayerLoop.SetPlayerLoop(loop);
        }

        static void EarlyUpdateTick()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < Bridges.Count; i++)
            {
                Bridges[i].PumpUpdate(dt);
            }
        }

        static void FixedUpdateTick()
        {
            float dt = Time.fixedDeltaTime;
            for (int i = 0; i < Bridges.Count; i++)
            {
                Bridges[i].PumpFixedUpdate(dt);
            }
        }
    }
}
