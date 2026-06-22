using System;
using System.Collections.Generic;
using Luau;
using Luau.Unity;
using Minetake.Basis.Luau.Registry;
using Minetake.Basis.Luau.Runtime;
using Minetake.Basis.Luau.Services;
using UnityEngine;

namespace Minetake.Basis.Luau
{
    public abstract class LuauHostBase : MonoBehaviour
    {
        protected abstract LuauHostKind DefaultHostKind { get; }

        readonly LuauCallbackRegistry _callbacks = new();
        readonly List<LuauScriptProxy> _proxies = new();
        readonly Dictionary<LuauScriptProxy, uint> _proxyIds = new();
        uint _nextProxyId = 1;

        LuauExecutionLimits _limits;
        LuauState _rootState;
        LuauHostRuntimeBridge _runtimeBridge;
        bool _stateDestroyed;
        static uint _nextHostId = 1;

        public LuauHostKind HostKind => DefaultHostKind;
        public LuauCallbackRegistry Callbacks => _callbacks;
        public LuauCapability Capability { get; private set; }
        public bool HasLiveState => _rootState != null && !_stateDestroyed;
        public LuauHostRuntimeBridge RuntimeBridge => _runtimeBridge;
        public LuauAuthorityTable Authority => _runtimeBridge?.Authority;

        internal LuauFunction LoadBytecodeProtected(LuauState thread, byte[] verifiedBytecode, string moduleName)
        {
            _limits.BeginProtectedLoad(thread);
            try
            {
                return thread.Load(verifiedBytecode, moduleName);
            }
            finally
            {
                _limits.EndProtectedLoad(thread);
            }
        }

        protected virtual void Awake()
        {
            Capability = new LuauCapability(HostKind, transform);
            _runtimeBridge = new LuauHostRuntimeBridge(this, _nextHostId++);
            InitializeStateIfNeeded();
        }

        protected void OnDestroy()
        {
            DestroyState(LuauDisableReason.Internal, "host destroyed");
        }

        public uint RegisterProxy(LuauScriptProxy proxy)
        {
            if (proxy == null)
            {
                throw new ArgumentNullException(nameof(proxy));
            }

            if (!_proxies.Contains(proxy))
            {
                _proxies.Add(proxy);
            }

            if (!_proxyIds.TryGetValue(proxy, out uint proxyId))
            {
                proxyId = _nextProxyId++;
                _proxyIds[proxy] = proxyId;
            }

            _runtimeBridge?.RegisterProxy(proxyId, proxy);
            return proxyId;
        }

        public void UnregisterProxy(LuauScriptProxy proxy)
        {
            if (proxy != null)
            {
                _proxies.Remove(proxy);
                _proxyIds.Remove(proxy);
                _callbacks.ClearProxy(proxy);
                _runtimeBridge?.UnregisterProxy(proxy);
            }
        }

        internal void InitializeStateIfNeeded()
        {
            if (_rootState != null || _stateDestroyed)
            {
                return;
            }

            var settings = BasisLuauRuntimeSettings.GetOrCreate();
            _limits = new LuauExecutionLimits();
            _runtimeBridge.EnsureNative(_limits, settings);
            _rootState = _runtimeBridge.Native.CreateRootStateWrapper();
            if (_rootState == null)
            {
                throw new InvalidOperationException(
                    "Basis Luau failed to wrap native root VM state. Ensure libluau.dll/luau.dll are deployed and BasisLuauNativeRuntime is available.");
            }

            _rootState.OpenLibraries();
            LuauSandbox.ApplyRoot(_rootState);
        }

        public LuauState CreateSandboxedThread()
        {
            InitializeStateIfNeeded();
            LuauState thread = _rootState.CreateThread();
            LuauSandbox.ApplyThread(thread);
            return thread;
        }

        public LuauObjectHandle RegisterObject(UnityEngine.Object obj) => _runtimeBridge.RegisterObject(obj);

        public bool TryResolveTransform(LuauObjectHandle handle, out Transform transform)
        {
            transform = null;
            if (!Capability.ValidateHandle(Authority, handle, typeof(Transform), out UnityEngine.Object obj))
            {
                return false;
            }

            transform = obj as Transform;
            return transform != null;
        }

        internal ProtectedCallResult InvokeLifecycle(LuauScriptProxy proxy, string methodName, ReadOnlySpan<LuauValue> args)
        {
            if (_stateDestroyed || proxy == null || !proxy.IsEnabled)
            {
                return default;
            }

            if (!proxy.TryGetFunction(methodName, out LuauFunction function))
            {
                return new ProtectedCallResult { Success = true, Reason = LuauDisableReason.None, RecoveredViaProtectedCall = true };
            }

            return InvokeProtected(proxy, function, args);
        }

        internal ProtectedCallResult InvokeCallback(LuauScriptProxy proxy, LuauFunction function, params LuauValue[] args)
        {
            if (_stateDestroyed || proxy == null || !proxy.IsEnabled || function == null)
            {
                return default;
            }

            return InvokeProtected(proxy, function, args);
        }

        internal ProtectedInvokeResult InvokeProtectedWithResults(LuauScriptProxy proxy, LuauFunction function, ReadOnlySpan<LuauValue> args)
        {
            if (_stateDestroyed || proxy == null || !proxy.IsEnabled || function == null)
            {
                return default;
            }

            return InvokeProtectedCore(proxy, function, args, handleFailure: true);
        }

        internal ProtectedInvokeResult InvokeProtectedModuleLoad(LuauScriptProxy proxy, LuauFunction function, ReadOnlySpan<LuauValue> args)
        {
            if (_stateDestroyed || proxy == null || function == null)
            {
                return default;
            }

            return InvokeProtectedCore(proxy, function, args, handleFailure: false);
        }

        ProtectedInvokeResult InvokeProtectedCore(LuauScriptProxy proxy, LuauFunction function, ReadOnlySpan<LuauValue> args, bool handleFailure)
        {
            try
            {
                LuauState state = proxy.ThreadState ?? _rootState;
                ProtectedInvokeResult result = _limits.InvokeProtectedWithResults(state, function, args);
                if (handleFailure)
                {
                    HandleFailure(proxy, result.Call);
                }

                return result;
            }
            catch (Exception ex)
            {
                var failure = new ProtectedInvokeResult
                {
                    Call = new ProtectedCallResult
                    {
                        Success = false,
                        Reason = LuauDisableReason.Internal,
                        ErrorMessage = ex.Message,
                        RecoveredViaProtectedCall = true,
                    },
                    ReturnValues = null,
                };

                if (handleFailure)
                {
                    HandleFailure(proxy, failure.Call);
                }

                return failure;
            }
        }

        ProtectedCallResult InvokeProtected(LuauScriptProxy proxy, LuauFunction function, ReadOnlySpan<LuauValue> args) =>
            InvokeProtectedWithResults(proxy, function, args).Call;

        internal void CompleteImageDownload(LuauScriptProxy proxy, BasisLuauImageDownloadResult result)
        {
            if (proxy == null || !proxy.IsEnabled)
            {
                return;
            }

            LuauState thread = proxy.ThreadState ?? CreateSandboxedThread();
            LuauTable table = thread.CreateTable();
            table["success"] = result.Success;
            table["error"] = result.Error ?? string.Empty;
            table["sizeInMemoryBytes"] = result.SizeInMemoryBytes;
            if (result.Result != null)
            {
                table["texture"] = RegisterObject(result.Result).ToRaw();
            }

            if (proxy.TryGetFunction("__imageCallback", out LuauFunction callback))
            {
                InvokeCallback(proxy, callback, table);
            }
        }

        void HandleFailure(LuauScriptProxy proxy, ProtectedCallResult result)
        {
            if (result.Success)
            {
                return;
            }

            switch (result.Reason)
            {
                case LuauDisableReason.Timeout when result.RecoveredViaProtectedCall:
                    DisableProxyOnly(proxy, LuauDisableReason.Timeout, result.ErrorMessage);
                    break;
                case LuauDisableReason.Timeout:
                    DestroyState(LuauDisableReason.Timeout, result.ErrorMessage);
                    break;
                case LuauDisableReason.AllocFailure:
                case LuauDisableReason.Panic:
                case LuauDisableReason.Internal:
                    DestroyState(result.Reason, result.ErrorMessage);
                    break;
                default:
                    DestroyState(LuauDisableReason.Internal, result.ErrorMessage);
                    break;
            }
        }

        public void DestroyState(LuauDisableReason reason, string message)
        {
            if (_stateDestroyed)
            {
                return;
            }

            _stateDestroyed = true;
            Authority?.BumpHostEpoch();

            _runtimeBridge?.Dispose();
            _runtimeBridge = null;

            for (int i = 0; i < _proxies.Count; i++)
            {
                _proxies[i].Disable(reason, message);
            }

            for (int i = 0; i < _proxies.Count; i++)
            {
                _proxies[i].ReleaseThreadState();
            }

            _rootState?.Dispose();
            _rootState = null;
            _limits?.Dispose();
            _limits = null;
        }

        public void DisableProxyOnly(LuauScriptProxy proxy, LuauDisableReason reason, string message)
        {
            proxy?.Disable(reason, message);
            _runtimeBridge?.BumpProxyGeneration(proxy);
        }
    }
}
