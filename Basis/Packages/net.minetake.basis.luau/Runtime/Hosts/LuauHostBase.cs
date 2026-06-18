using System;
using System.Collections.Generic;
using Luau;
using Luau.Unity;
using Minetake.Basis.Luau.Bindings;
using Minetake.Basis.Luau.Policy;
using Minetake.Basis.Luau.Registry;
using UnityEngine;

namespace Minetake.Basis.Luau
{
    public abstract class LuauHostBase : MonoBehaviour
    {
        protected abstract LuauHostKind DefaultHostKind { get; }

        readonly LuauObjectRegistry _registry = new();
        readonly LuauCallbackRegistry _callbacks = new();
        readonly List<LuauScriptProxy> _proxies = new();

        LuauExecutionLimits _limits;
        LuauState _rootState;
        bool _stateDestroyed;

        public LuauHostKind HostKind => DefaultHostKind;
        public LuauObjectRegistry Registry => _registry;
        public LuauCallbackRegistry Callbacks => _callbacks;
        public LuauCapability Capability { get; private set; }
        public LuauWhitelistPolicy Policy { get; private set; }
        public bool HasLiveState => _rootState != null && !_stateDestroyed;

        protected virtual void Awake()
        {
            Policy = LuauWhitelistPolicy.ForHost(HostKind);
            Capability = new LuauCapability(HostKind, transform, Policy);
            InitializeStateIfNeeded();
        }

        protected void OnDestroy()
        {
            DestroyState(LuauDisableReason.Internal, "host destroyed");
        }

        public void RegisterProxy(LuauScriptProxy proxy)
        {
            if (proxy == null)
            {
                return;
            }

            if (!_proxies.Contains(proxy))
            {
                _proxies.Add(proxy);
            }
        }

        public void UnregisterProxy(LuauScriptProxy proxy)
        {
            if (proxy != null)
            {
                _proxies.Remove(proxy);
                _callbacks.ClearProxy(proxy);
            }
        }

        internal void InitializeStateIfNeeded()
        {
            if (_rootState != null || _stateDestroyed)
            {
                return;
            }

            Policy ??= LuauWhitelistPolicy.ForHost(HostKind);
            _limits = new LuauExecutionLimits();
            _rootState = _limits.CreateLimitedState();
            RegisterStandardLibraries(_rootState);
            RegisterHostBindings(_rootState);
            LuauSandbox.ApplyRoot(_rootState);
        }

        protected virtual void RegisterStandardLibraries(LuauState state)
        {
            state.OpenLibraries();
        }

        protected virtual void RegisterHostBindings(LuauState state)
        {
            state.OpenLibrary<TransformBindings>();
            state.OpenLibrary<TimeBindings>();
            ObjectBindings.Install(state);
            RegisterServiceBindings(state);
        }

        protected abstract void RegisterServiceBindings(LuauState state);

        public LuauState CreateSandboxedThread()
        {
            InitializeStateIfNeeded();
            LuauState thread = _rootState.CreateThread();
            LuauSandbox.ApplyThread(thread);
            return thread;
        }

        public LuauObjectHandle RegisterObject(UnityEngine.Object obj) => _registry.Register(obj);

        public bool TryResolveTransform(LuauObjectHandle handle, out Transform transform)
        {
            transform = null;
            if (!Capability.ValidateHandle(_registry, handle, typeof(Transform), out UnityEngine.Object obj))
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
            LuauBindingContext.Host = this;
            LuauBindingContext.Proxy = proxy;
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
            finally
            {
                LuauBindingContext.Clear();
            }
        }

        ProtectedCallResult InvokeProtected(LuauScriptProxy proxy, LuauFunction function, ReadOnlySpan<LuauValue> args)
        {
            return InvokeProtectedWithResults(proxy, function, args).Call;
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
                    proxy.Disable(LuauDisableReason.Timeout, result.ErrorMessage);
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
            for (int i = 0; i < _proxies.Count; i++)
            {
                _proxies[i].Disable(reason, message);
            }

            for (int i = 0; i < _proxies.Count; i++)
            {
                _proxies[i].ReleaseThreadState();
            }

            _registry.Dispose();
            _rootState?.Dispose();
            _rootState = null;
            _limits?.Dispose();
            _limits = null;
        }

        public void DisableProxyOnly(LuauScriptProxy proxy, LuauDisableReason reason, string message)
        {
            proxy?.Disable(reason, message);
        }
    }
}
