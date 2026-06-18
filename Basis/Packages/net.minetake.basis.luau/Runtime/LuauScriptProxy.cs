using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Luau;
using Luau.Unity;
using Minetake.Basis.Luau.Registry;
using UnityEngine;

namespace Minetake.Basis.Luau
{
    [DisallowMultipleComponent]
    public sealed class LuauScriptProxy : MonoBehaviour
    {
        static readonly string[] LifecycleMethods =
        {
            "awake", "start", "update", "fixedUpdate", "lateUpdate", "onEnable", "onDisable", "onDestroy",
        };

        [SerializeField] LuauHostKind requiredHostKind;
        [SerializeField] byte[] bytecode;
        [SerializeField] ulong[] handleRaws = Array.Empty<ulong>();
        [SerializeField] string moduleName = "script";

        LuauHostBase _host;
        LuauState _thread;
        LuauTable _module;
        readonly Dictionary<string, LuauFunction> _functions = new(StringComparer.Ordinal);
        bool _enabled = true;
        bool _loaded;
        string _disableReason;

        public bool IsEnabled => _enabled && _loaded;
        public LuauHostKind RequiredHostKind => requiredHostKind;
        public byte[] Bytecode => bytecode;

        public void Configure(LuauHostKind hostKind, byte[] compiledBytecode, UnityEngine.Object[] slots, string name)
        {
            requiredHostKind = hostKind;
            bytecode = compiledBytecode;
            moduleName = string.IsNullOrWhiteSpace(name) ? "script" : name;
            handleRaws = RegisterSlotObjects(slots);
        }

        void Awake()
        {
            TryLoad();
            _host?.InvokeLifecycle(this, "awake", ReadOnlySpan<LuauValue>.Empty);
        }

        void Start()
        {
            _host?.InvokeLifecycle(this, "start", ReadOnlySpan<LuauValue>.Empty);
        }

        void Update()
        {
            _host?.InvokeLifecycle(this, "update", ReadOnlySpan<LuauValue>.Empty);
        }

        void FixedUpdate()
        {
            _host?.InvokeLifecycle(this, "fixedUpdate", ReadOnlySpan<LuauValue>.Empty);
        }

        void LateUpdate()
        {
            _host?.InvokeLifecycle(this, "lateUpdate", ReadOnlySpan<LuauValue>.Empty);
        }

        void OnEnable()
        {
            _host?.InvokeLifecycle(this, "onEnable", ReadOnlySpan<LuauValue>.Empty);
        }

        void OnDisable()
        {
            _host?.InvokeLifecycle(this, "onDisable", ReadOnlySpan<LuauValue>.Empty);
        }

        void OnDestroy()
        {
            _host?.InvokeLifecycle(this, "onDestroy", ReadOnlySpan<LuauValue>.Empty);
            _host?.UnregisterProxy(this);
            _thread?.Dispose();
            _thread = null;
            _module = null;
            _functions.Clear();
        }

        public bool TryGetFunction(string name, out LuauFunction function) => _functions.TryGetValue(name, out function);

        public void Disable(LuauDisableReason reason, string message)
        {
            _enabled = false;
            _disableReason = $"{reason}: {message}";
            _functions.Clear();
        }

        bool TryLoad()
        {
            if (_loaded || !_enabled)
            {
                return _loaded;
            }

            _host = GetComponentInParent<LuauHostBase>(true);
            if (_host == null)
            {
                Disable(LuauDisableReason.Internal, "no LuauHostBase in parent hierarchy");
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

            _host.InitializeStateIfNeeded();
            _host.RegisterProxy(this);

            _thread = _host.CreateSandboxedThread();
            LuauFunction chunk = _thread.Load(bytecode, moduleName);
            LuauValue[] results = chunk.InvokeAsync(Array.Empty<LuauValue>()).AsTask().GetAwaiter().GetResult();
            if (results == null || results.Length == 0 || results[0].Type != LuauType.Table)
            {
                Disable(LuauDisableReason.Internal, "module must export a table");
                return false;
            }

            _module = results[0].Read<LuauTable>();
            InjectHandleGlobals();
            CacheLifecycleFunctions();
            _loaded = true;
            return true;
        }

        ulong[] RegisterSlotObjects(UnityEngine.Object[] slots)
        {
            if (slots == null || slots.Length == 0)
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
