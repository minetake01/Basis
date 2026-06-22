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
    [DisallowMultipleComponent]
    public sealed class LuauScriptProxy : MonoBehaviour
    {
        static readonly string[] LifecycleMethods =
        {
            "awake", "start", "update", "fixedUpdate", "lateUpdate", "onEnable", "onDisable", "onDestroy",
            "onTriggerEnter", "onTriggerExit", "onCollisionEnter", "onCollisionExit",
        };

        static readonly LuauValue[] EmptyArgs = Array.Empty<LuauValue>();

        [SerializeField] LuauHostKind requiredHostKind;
        [SerializeField] LuauHostBase boundHost;
        [SerializeField] byte[] bytecode;
        [SerializeField] UnityEngine.Object[] slotObjects = Array.Empty<UnityEngine.Object>();
        [SerializeField] ulong[] handleRaws = Array.Empty<ulong>();
        [SerializeField] string moduleName = "script";

        LuauHostBase _host;
        LuauState _thread;
        LuauTable _module;
        readonly Dictionary<string, LuauFunction> _functions = new(StringComparer.Ordinal);
        bool _enabled = true;
        bool _loaded;
        bool _awakeInvoked;
        bool _onEnableInvoked;
        string _disableReason;
        uint _proxyGeneration = 1;

        public bool IsEnabled => _enabled && _loaded;
        public LuauHostKind RequiredHostKind => requiredHostKind;
        public LuauHostBase BoundHost => boundHost;
        public byte[] Bytecode => bytecode;
        public uint ProxyId { get; private set; }
        public uint ProxyGeneration => _proxyGeneration;
        public bool IsNativeRegistered { get; private set; }
        public string DisableReason => _disableReason;
        internal LuauState ThreadState => _thread;

        public struct ProxyGenerationState
        {
            public uint Generation { get; set; }
            public bool Disabled { get; set; }
        }

        public void Configure(
            LuauHostKind hostKind,
            byte[] compiledBytecode,
            UnityEngine.Object[] slots,
            string name,
            LuauHostBase host)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            requiredHostKind = hostKind;
            boundHost = host;
            bytecode = compiledBytecode;
            slotObjects = slots ?? Array.Empty<UnityEngine.Object>();
            moduleName = string.IsNullOrWhiteSpace(name) ? "script" : name;
            handleRaws = Array.Empty<ulong>();
            if (Application.isPlaying && isActiveAndEnabled)
            {
                if (TryLoad())
                {
                    InvokeAwakeIfNeeded();
                    InvokeOnEnableIfNeeded();
                }
            }
        }

        void Awake()
        {
            if (!HasConfiguration())
            {
                return;
            }

            if (TryLoad())
            {
                InvokeAwakeIfNeeded();
            }
        }

        void Start()
        {
            if (!_loaded && HasConfiguration())
            {
                if (TryLoad())
                {
                    InvokeAwakeIfNeeded();
                    InvokeOnEnableIfNeeded();
                }
            }

            if (!_loaded)
            {
                if (!HasConfiguration())
                {
                    Debug.LogError($"[BasisLuau] LuauScriptProxy on '{name}' has no boundHost/bytecode configuration.");
                    Disable(LuauDisableReason.Internal, "missing configuration");
                }

                return;
            }

            _host?.InvokeLifecycle(this, "start", EmptyArgs);
        }

        void OnEnable() => InvokeOnEnableIfNeeded();

        void OnDisable()
        {
            if (!_onEnableInvoked)
            {
                return;
            }

            _host?.InvokeLifecycle(this, "onDisable", EmptyArgs);
            _onEnableInvoked = false;
        }

        void OnDestroy()
        {
            _host?.InvokeLifecycle(this, "onDestroy", EmptyArgs);
            _host?.UnregisterProxy(this);
            BasisLuauOscSubscribeRegistry.ClearProxy(ProxyId);
            ReleaseThreadState();
        }

        internal void ReleaseThreadState()
        {
            IsNativeRegistered = false;
            _thread?.Dispose();
            _thread = null;
            _module = null;
            _functions.Clear();
        }

        void OnTriggerEnter(Collider other) => InvokePhysics("onTriggerEnter", other);

        void OnTriggerExit(Collider other) => InvokePhysics("onTriggerExit", other);

        void OnCollisionEnter(Collision collision) => InvokePhysics("onCollisionEnter", collision.collider);

        void OnCollisionExit(Collision collision) => InvokePhysics("onCollisionExit", collision.collider);

        void InvokePhysics(string method, Collider other)
        {
            if (_host == null || other == null || !IsEnabled)
            {
                return;
            }

            LuauObjectHandle handle = _host.RegisterObject(other);
            LuauCommandType eventType = method switch
            {
                "onTriggerEnter" => LuauCommandType.EventTriggerEnter,
                "onTriggerExit" => LuauCommandType.EventTriggerExit,
                "onCollisionEnter" => LuauCommandType.EventCollisionEnter,
                "onCollisionExit" => LuauCommandType.EventCollisionExit,
                _ => LuauCommandType.Invalid,
            };

            if (eventType == LuauCommandType.Invalid)
            {
                return;
            }

            _host.RuntimeBridge?.Events.TryEnqueue(
                eventType,
                ProxyId,
                ProxyGeneration,
                handle.Index,
                handle.Generation);
        }

        public bool TryGetFunction(string name, out LuauFunction function) => _functions.TryGetValue(name, out function);

        public void Disable(LuauDisableReason reason, string message)
        {
            _enabled = false;
            IsNativeRegistered = false;
            _proxyGeneration++;
            _disableReason = $"{reason}: {message}";
            _functions.Clear();
            _host?.RuntimeBridge?.BumpProxyGeneration(this);
        }

        void InvokeAwakeIfNeeded()
        {
            if (_awakeInvoked)
            {
                return;
            }

            _awakeInvoked = true;
            _host?.InvokeLifecycle(this, "awake", EmptyArgs);
        }

        void InvokeOnEnableIfNeeded()
        {
            if (_onEnableInvoked || !_loaded)
            {
                return;
            }

            _onEnableInvoked = true;
            _host?.InvokeLifecycle(this, "onEnable", EmptyArgs);
        }

        bool HasConfiguration() => boundHost != null && bytecode is { Length: > 0 };

        bool TryLoad()
        {
            if (_loaded || !_enabled)
            {
                return _loaded;
            }

            if (!HasConfiguration())
            {
                return false;
            }

            _host = boundHost;
            if (_host == null)
            {
                Disable(LuauDisableReason.Internal, "boundHost is not assigned");
                return false;
            }

            if (_host.HostKind != requiredHostKind)
            {
                Disable(LuauDisableReason.Internal, $"host kind mismatch: expected {requiredHostKind}, got {_host.HostKind}");
                return false;
            }

            if (bytecode == null || bytecode.Length == 0)
            {
                Disable(LuauDisableReason.Internal, "bytecode missing or empty");
                return false;
            }

            LuauBytecodeGate.GateResult gate = LuauBytecodeGate.ValidateForLoad(bytecode);
            if (!gate.Success)
            {
                Debug.LogWarning($"[BasisLuau] Script '{name}' failed bytecode gate: {gate.Message}");
                Disable(MapReason(gate.Reason), gate.Message);
                return false;
            }

            ReadOnlyMemory<byte> verifiedBytecode = gate.VerifiedBytecode;

            _host.InitializeStateIfNeeded();
            ProxyId = _host.RegisterProxy(this);
            handleRaws = RegisterSlotObjects(slotObjects);

            _thread = _host.CreateSandboxedThread();
            _host.RuntimeBridge.Native.SetThreadContext(_thread, ProxyId, ProxyGeneration);
            LuauFunction chunk = _host.LoadBytecodeProtected(_thread, verifiedBytecode.ToArray(), moduleName);
            ProtectedInvokeResult invokeResult = _host.InvokeProtectedModuleLoad(this, chunk, EmptyArgs);
            if (!invokeResult.Call.Success)
            {
                string error = string.IsNullOrWhiteSpace(invokeResult.Call.ErrorMessage)
                    ? "unknown module initialization failure"
                    : invokeResult.Call.ErrorMessage;
                Debug.LogError(
                    $"[BasisLuau] Script '{moduleName}' failed module initialization: {invokeResult.Call.Reason}: {error}",
                    this);
                ReleaseThreadState();
                Disable(invokeResult.Call.Reason, error);
                return false;
            }

            LuauValue[] results = invokeResult.ReturnValues;
            if (results == null || results.Length == 0 || results[0].Type != LuauType.Table)
            {
                ReleaseThreadState();
                Disable(LuauDisableReason.Internal, "module must export a table");
                return false;
            }

            _module = results[0].Read<LuauTable>();
            InjectHandleGlobals();
            CacheLifecycleFunctions();
            _loaded = true;
            IsNativeRegistered = _host.RuntimeBridge.RegisterNativeProxy(
                this,
                _thread,
                GetCachedFunction("update"),
                GetCachedFunction("fixedUpdate"),
                GetCachedFunction("lateUpdate"));
            if (!IsNativeRegistered)
            {
                _loaded = false;
                ReleaseThreadState();
                Disable(LuauDisableReason.Internal, "native proxy registration failed");
                return false;
            }

            _host.RuntimeBridge?.PublishInitialSnapshots();
            return true;
        }

        LuauFunction GetCachedFunction(string name) =>
            _functions.TryGetValue(name, out LuauFunction fn) ? fn : null;

        static LuauDisableReason MapReason(LuauFailureReason reason) => reason switch
        {
            LuauFailureReason.BytecodeRejected => LuauDisableReason.Internal,
            LuauFailureReason.SignatureRejected => LuauDisableReason.Internal,
            _ => LuauDisableReason.Internal,
        };

        internal static ReadOnlySpan<LuauValue> SpanWithDeltaTime(float value) => SpanWith(value);

        static ReadOnlySpan<LuauValue> SpanWith(double value)
        {
            _singleArg ??= new LuauValue[1];
            _singleArg[0] = value;
            return _singleArg;
        }

        [ThreadStatic] static LuauValue[] _singleArg;

        static ReadOnlySpan<LuauValue> SpanWith(double a, double b)
        {
            _doubleArg ??= new LuauValue[2];
            _doubleArg[0] = a;
            _doubleArg[1] = b;
            return _doubleArg;
        }

        [ThreadStatic] static LuauValue[] _doubleArg;

        ulong[] RegisterSlotObjects(UnityEngine.Object[] slots)
        {
            if (slots == null || slots.Length == 0 || _host == null)
            {
                return Array.Empty<ulong>();
            }

            var raws = new ulong[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                UnityEngine.Object slot = slots[i];
                raws[i] = slot != null ? _host.RegisterObject(slot).ToRaw() : LuauObjectHandle.InvalidRaw;
            }

            return raws;
        }

        void InjectHandleGlobals()
        {
            if (handleRaws == null || handleRaws.Length == 0)
            {
                return;
            }

            LuauTable table = _thread.CreateTable();
            for (int i = 0; i < handleRaws.Length; i++)
            {
                table[i + 1] = handleRaws[i];
            }

            _thread["__luau_handles"] = table;
        }

        void CacheLifecycleFunctions()
        {
            _functions.Clear();
            for (int i = 0; i < LifecycleMethods.Length; i++)
            {
                string name = LifecycleMethods[i];
                LuauValue value = _module[name];
                if (value.Type == LuauType.Funciton)
                {
                    _functions[name] = value.Read<LuauFunction>();
                }
            }
        }
    }
}
