using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Luau;
using Luau.Unity;
using Minetake.Basis.Luau.Registry;
using Minetake.Basis.Luau.Services;
using UnityEngine;

namespace Minetake.Basis.Luau.Runtime
{
    public sealed class LuauHostRuntimeBridge : IDisposable
    {
        readonly LuauHostBase _host;
        readonly LuauAuthorityTable _authority;
        readonly LuauEventIngress _events;
        readonly LuauTicketRegistry _tickets = new();
        readonly LuauTicketProcessor _ticketProcessor;
        readonly Dictionary<uint, LuauScriptProxy.ProxyGenerationState> _proxyGenerations = new();
        BasisLuauNativeRuntime _native;
        bool _disposed;

        public LuauAuthorityTable Authority => _authority;
        public BasisLuauNativeRuntime Native => _native;
        public LuauTicketRegistry Tickets => _tickets;
        public LuauEventIngress Events => _events;

        public LuauHostRuntimeBridge(LuauHostBase host, uint hostId)
        {
            _host = host;
            _authority = new LuauAuthorityTable(hostId, host.transform, host.HostKind);
            _events = new LuauEventIngress(hostId);
            _ticketProcessor = new LuauTicketProcessor(host, _authority, _tickets);
            LuauBridgePump.Register(this);
        }

        public void EnsureNative(LuauExecutionLimits limits, BasisLuauRuntimeSettings settings)
        {
            if (_native != null && _native.IsCreated)
            {
                return;
            }

            if (!BasisLuauNativeRuntime.IsAvailable)
            {
                throw new InvalidOperationException(
                    $"Basis Luau native runtime is required: {BasisLuauNativeRuntime.UnavailableReason}");
            }

            _native = new BasisLuauNativeRuntime();
            _native.Create(limits, (ushort)_authority.HostId, (int)_host.HostKind);
            _ticketProcessor.SetNative(_native);
#if UNITY_EDITOR
            _native.StartScheduler(0);
#else
            int workers = Mathf.Clamp(settings.maxWorkers, 0, 4);
            _native.StartScheduler(workers);
#endif
        }

        public void RegisterProxy(uint proxyId, LuauScriptProxy proxy)
        {
            if (proxy == null)
            {
                throw new ArgumentNullException(nameof(proxy));
            }

            if (proxyId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(proxyId), "Proxy ID must be assigned before registration.");
            }

            _proxyGenerations[proxyId] = new LuauScriptProxy.ProxyGenerationState
            {
                Generation = proxy.ProxyGeneration,
                Disabled = false,
            };
            _ticketProcessor.RegisterProxy(proxyId, proxy);
        }

        public void UnregisterProxy(LuauScriptProxy proxy)
        {
            if (proxy == null)
            {
                return;
            }

            _native?.UnregisterProxy(proxy.ProxyId);
            _proxyGenerations.Remove(proxy.ProxyId);
            _ticketProcessor.UnregisterProxy(proxy.ProxyId);
        }

        public void BumpProxyGeneration(LuauScriptProxy proxy)
        {
            if (proxy == null)
            {
                return;
            }

            if (_proxyGenerations.TryGetValue(proxy.ProxyId, out LuauScriptProxy.ProxyGenerationState state))
            {
                state.Generation = proxy.ProxyGeneration;
                state.Disabled = true;
                _proxyGenerations[proxy.ProxyId] = state;
            }

            _native?.SetProxyGeneration(proxy.ProxyId, proxy.ProxyGeneration);
        }

        public bool RegisterNativeProxy(LuauScriptProxy proxy, LuauState thread, LuauFunction update, LuauFunction fixedUpdate, LuauFunction lateUpdate)
        {
            if (_native == null)
            {
                throw new InvalidOperationException("Native runtime must be initialized before proxy registration.");
            }

            if (proxy == null || !proxy.IsEnabled || proxy.ProxyId == 0 || thread == null)
            {
                throw new InvalidOperationException("Proxy must be loaded, identified, and bound to a thread before native registration.");
            }

            proxy.TryGetFunction("onTriggerEnter", out LuauFunction triggerEnter);
            proxy.TryGetFunction("onTriggerExit", out LuauFunction triggerExit);
            proxy.TryGetFunction("onCollisionEnter", out LuauFunction collisionEnter);
            proxy.TryGetFunction("onCollisionExit", out LuauFunction collisionExit);

            int refUpdate = LuauFunctionRefHelper.GetReference(update);
            int refFixedUpdate = LuauFunctionRefHelper.GetReference(fixedUpdate);
            int refLateUpdate = LuauFunctionRefHelper.GetReference(lateUpdate);

            if (update != null && refUpdate <= 0)
            {
                return false;
            }

            if (fixedUpdate != null && refFixedUpdate <= 0)
            {
                return false;
            }

            if (lateUpdate != null && refLateUpdate <= 0)
            {
                return false;
            }

            return _native.RegisterProxy(
                proxy.ProxyId,
                thread,
                proxy.ProxyGeneration,
                refUpdate,
                refFixedUpdate,
                refLateUpdate,
                LuauFunctionRefHelper.GetReference(triggerEnter),
                LuauFunctionRefHelper.GetReference(triggerExit),
                LuauFunctionRefHelper.GetReference(collisionEnter),
                LuauFunctionRefHelper.GetReference(collisionExit));
        }

        public LuauObjectHandle RegisterObject(UnityEngine.Object target) => _authority.Register(target);

        public bool TryResolve(UnityEngine.Object target, out LuauObjectHandle handle)
        {
            handle = default;
            if (target == null)
            {
                return false;
            }

            handle = _authority.Register(target);
            return true;
        }

        public void PumpUpdate(float dt)
        {
            if (_disposed || _host == null)
            {
                return;
            }

            FlushCommands();
            _ticketProcessor.ProcessPending();
            PublishSnapshots(dt);
            _events.DrainToNative(_native);

            if (_native != null && _native.IsCreated)
            {
                _native.KickScheduler(dt, Time.fixedDeltaTime, false);
            }
        }

        public void PumpFixedUpdate(float fixedDt)
        {
            if (_disposed || _host == null || _native == null)
            {
                return;
            }

            PublishSnapshots(fixedDt, fixedOnly: true);
            _native.KickScheduler(0, fixedDt, true);
        }

        void FlushCommands()
        {
            if (_native == null || !_native.IsCreated)
            {
                return;
            }

            while (true)
            {
                BasisLuauRingResult result = _native.PopCommand(out BasisLuauCommandNative cmd);
                if (result != BasisLuauRingResult.Ok)
                {
                    break;
                }

                LuauCommandFlush.Apply(_host, _authority, _native, _ticketProcessor, _proxyGenerations, cmd);
            }
        }

        void PublishSnapshots(float dt, bool fixedOnly = false)
        {
            if (_native == null || !_native.IsCreated)
            {
                return;
            }

            if (!_native.BeginSnapshotPublish(out ulong epoch))
            {
                return;
            }

            unsafe
            {
                var timeSlot = new BasisLuauSnapshotSlotNative();
                timeSlot.HandleIndex = BasisLuauSnapshotSlotNative.TimeSlotIndex;
                timeSlot.TimeData[0] = fixedOnly ? 0 : dt;
                timeSlot.TimeData[1] = Time.fixedDeltaTime;
                timeSlot.TimeData[2] = Time.time;
                timeSlot.TimeData[3] = Time.unscaledDeltaTime;
                _native.WriteSnapshotSlot(BasisLuauSnapshotSlotNative.TimeSlotIndex, ref timeSlot);
                PublishDatetimeSnapshot();

                foreach (KeyValuePair<uint, LuauAuthorityTable.Entry> pair in _authority.EnumerateEntries())
                {
                    if (pair.Value.Tombstoned || pair.Value.Target == null)
                    {
                        continue;
                    }

                    if (pair.Value.Target is not Transform transform)
                    {
                        continue;
                    }

                    var slot = new BasisLuauSnapshotSlotNative
                    {
                        HandleIndex = pair.Key,
                        HandleGeneration = pair.Value.Generation,
                        HostId = _authority.HostId,
                    };
                    Vector3 p = transform.position;
                    Quaternion r = transform.rotation;
                    slot.Position[0] = p.x;
                    slot.Position[1] = p.y;
                    slot.Position[2] = p.z;
                    slot.Rotation[0] = r.x;
                    slot.Rotation[1] = r.y;
                    slot.Rotation[2] = r.z;
                    slot.Rotation[3] = r.w;
                    _native.WriteSnapshotSlot(pair.Key, ref slot);
                }
            }

            _native.EndSnapshotPublish(epoch);
        }

        void PublishDatetimeSnapshot()
        {
            string nowText = DateTime.Now.ToString("G", CultureInfo.CurrentCulture);
            byte[] bytes = Encoding.UTF8.GetBytes(nowText);
            if (!_native.TryAllocBuffer(_authority.HostId, (uint)bytes.Length + 1, out BasisLuauBufferRefNative bufferRef, out IntPtr bytesPtr))
            {
                return;
            }

            unsafe
            {
                byte* target = (byte*)bytesPtr;
                for (int i = 0; i < bytes.Length; i++)
                {
                    target[i] = bytes[i];
                }

                target[bytes.Length] = 0;
                bufferRef.Length = (uint)bytes.Length;
            }

            _native.SetDatetimeBuffer(ref bufferRef);
        }

        public void PublishInitialSnapshots()
        {
            PublishSnapshots(Time.deltaTime);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            LuauBridgePump.Unregister(this);
            _native?.Dispose();
            _native = null;
        }
    }
}
