using System;
using System.Collections.Generic;
using HVR.Basis.Comms.OSC;
using Luau.Unity;
using Minetake.Basis.Luau.Services;
using UnityEngine;

namespace Minetake.Basis.Luau.Runtime
{
    public static class BasisLuauOscSubscribeRegistry
    {
        sealed class Subscription
        {
            public uint ProxyId;
            public uint ProxyGeneration;
            public int CallbackRef;
            public BasisLuauOscHost.OscMessageEvent Handler;
        }

        static readonly Dictionary<string, List<Subscription>> Subscriptions = new();

        public static void Subscribe(
            LuauHostBase host,
            LuauScriptProxy proxy,
            string address,
            int callbackRef)
        {
            if (host == null || proxy == null || string.IsNullOrEmpty(address) || callbackRef <= 0)
            {
                return;
            }

            BasisLuauOscHost oscHost = BasisLuauOscHandlers.GetOrCreateHost(host);
            if (oscHost == null)
            {
                return;
            }

            var sub = new Subscription
            {
                ProxyId = proxy.ProxyId,
                ProxyGeneration = proxy.ProxyGeneration,
                CallbackRef = callbackRef,
            };

            sub.Handler = (message, _) => Dispatch(host, sub, message);
            oscHost.Subscribe(address, sub.Handler);

            if (!Subscriptions.TryGetValue(address, out List<Subscription> list))
            {
                list = new List<Subscription>();
                Subscriptions[address] = list;
            }

            list.Add(sub);
        }

        static unsafe void Dispatch(LuauHostBase host, Subscription sub, OscMessage message)
        {
            if (host?.RuntimeBridge?.Native == null || !host.RuntimeBridge.Native.IsCreated)
            {
                return;
            }

            float value = 0;
            if (message.Arguments != null && message.Arguments.Length > 0)
            {
                value = message.Arguments[0].FloatValue;
            }

            var cmd = new BasisLuauCommandNative
            {
                Type = (ushort)LuauCommandType.EventOscMessage,
                HostId = (ushort)host.RuntimeBridge.Authority.HostId,
                ProxyId = sub.ProxyId,
                ProxyGeneration = sub.ProxyGeneration,
            };

            unsafe
            {
                cmd.Data[0] = sub.CallbackRef;
                cmd.Data[1] = value;
                string address = message.NormalizedPath ?? message.Path ?? string.Empty;
                if (host.RuntimeBridge.Native.TryAllocBuffer(
                        host.RuntimeBridge.Authority.HostId,
                        (uint)address.Length + 1,
                        out BasisLuauBufferRefNative bufferRef,
                        out System.IntPtr bytesPtr))
                {
                    byte* bytes = (byte*)bytesPtr;
                    int written = System.Text.Encoding.UTF8.GetBytes(address, new Span<byte>(bytes, (int)bufferRef.Length));
                    bytes[written] = 0;
                    cmd.Data[2] = bufferRef.Slot;
                    cmd.Data[3] = bufferRef.Generation;
                }
            }

            host.RuntimeBridge.Native.PushEvent(host.RuntimeBridge.Authority.HostId, ref cmd);
        }

        public static void ClearProxy(uint proxyId)
        {
            foreach (KeyValuePair<string, List<Subscription>> pair in Subscriptions)
            {
                pair.Value.RemoveAll(s => s.ProxyId == proxyId);
            }
        }
    }
}
