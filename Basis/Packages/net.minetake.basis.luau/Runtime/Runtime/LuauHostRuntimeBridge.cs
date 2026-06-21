using System;
using System.Collections.Generic;
using Luau.Unity;
using UnityEngine;

namespace Minetake.Basis.Luau.Runtime
{
    public sealed class LuauHostRuntimeBridge : IDisposable
    {
        readonly LuauHostBase _host;
        readonly LuauAuthorityTable _authority;
        readonly LuauEventIngress _events;
        readonly LuauTicketRegistry _tickets = new();
        readonly Dictionary<uint, LuauWorkerShadowState> _shadowByProxy = new();
        BasisLuauNativeRuntime _native;
        bool _disposed;

        public LuauAuthorityTable Authority => _authority;
        public BasisLuauNativeRuntime Native => _native;

        public LuauHostRuntimeBridge(LuauHostBase host, uint hostId)
        {
            _host = host;
            _authority = new LuauAuthorityTable(hostId, host.transform, host.HostKind);
            _events = new LuauEventIngress(hostId);
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
                return;
            }

            _native = new BasisLuauNativeRuntime();
            _native.Create(limits);
            if (settings.useWorkerScheduler)
            {
                int workers = Mathf.Clamp(settings.maxWorkers, 1, 4);
                _native.StartScheduler(workers);
            }
        }

        internal LuauWorkerShadowState GetShadow(uint proxyId)
        {
            if (!_shadowByProxy.TryGetValue(proxyId, out LuauWorkerShadowState shadow))
            {
                shadow = new LuauWorkerShadowState();
                _shadowByProxy[proxyId] = shadow;
            }

            return shadow;
        }

        public void PumpUpdate(float dt)
        {
            if (_disposed || _host == null)
            {
                return;
            }

            FlushCommands();
            PublishSnapshots();
            _events.DrainToNative(_native);

            if (BasisLuauRuntimeSettings.GetOrCreate().useWorkerScheduler && _native != null && _native.IsCreated)
            {
                _native.KickScheduler();
                _host.PumpLifecycleUpdate(dt);
            }
        }

        public void PumpFixedUpdate(float fixedDt)
        {
            if (_disposed || _host == null)
            {
                return;
            }

            if (BasisLuauRuntimeSettings.GetOrCreate().useWorkerScheduler)
            {
                _host.PumpLifecycleFixedUpdate(fixedDt);
            }
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

                LuauCommandFlush.Apply(_host, _authority, cmd);
            }
        }

        void PublishSnapshots()
        {
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

    public sealed class LuauWorkerShadowState
    {
        readonly Dictionary<uint, Vector3> _positions = new();

        public void SetPosition(uint handleIndex, Vector3 value) => _positions[handleIndex] = value;

        public bool TryGetPosition(uint handleIndex, out Vector3 value) => _positions.TryGetValue(handleIndex, out value);
    }
}
