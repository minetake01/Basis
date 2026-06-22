using System;
using System.Runtime.InteropServices;
using Luau;
using Luau.Native;
using Luau.Unity;
using UnityEngine;

namespace Luau.Unity
{
    public enum BasisLuauVerifyError
    {
        Ok = 0,
        Empty = 1,
        Truncated = 2,
        BadVersion = 3,
        BadTypeVersion = 4,
        LimitExceeded = 5,
        Malformed = 6,
    }

    public enum BasisLuauRingResult
    {
        Ok = 0,
        Full = 1,
        Empty = 2,
        Invalid = 3,
    }

    public enum BasisLuauProxyTickKind
    {
        Update = 0,
        FixedUpdate = 1,
        LateUpdate = 2,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 32)]
    public struct BasisLuauCommandNative
    {
        public ushort Type;
        public ushort HostId;
        public uint HandleIndex;
        public uint HandleGeneration;
        public uint ProxyId;
        public uint ProxyGeneration;
        public unsafe fixed float Data[4];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BasisLuauSnapshotSlotNative
    {
        public const uint TimeSlotIndex = 0;

        public uint HandleIndex;
        public uint HandleGeneration;
        public uint HostId;
        public unsafe fixed float Position[3];
        public unsafe fixed float Rotation[4];
        public unsafe fixed float TimeData[4];
        public uint Epoch;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BasisLuauBufferRefNative
    {
        public uint PoolId;
        public uint Slot;
        public uint Generation;
        public uint HostId;
        public uint Length;
    }

    public sealed unsafe class BasisLuauNativeRuntime : IDisposable
    {
#if (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR
        const string DllName = "__Internal";
#else
        const string DllName = "basis_luau_runtime";
#endif

        [DllImport(DllName, EntryPoint = "basis_luau_verify_bytecode", CallingConvention = CallingConvention.Cdecl)]
        public static extern BasisLuauVerifyError VerifyBytecode(byte* data, nuint size, nuint maxBytes);

        [DllImport(DllName, EntryPoint = "basis_luau_runtime_create", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr RuntimeCreate(ref BasisLuauLimitsConfig config, basis_luau_init_error* outError);

        [DllImport(DllName, EntryPoint = "basis_luau_runtime_destroy", CallingConvention = CallingConvention.Cdecl)]
        static extern void RuntimeDestroy(IntPtr rt);

        [DllImport(DllName, EntryPoint = "basis_luau_runtime_root_state", CallingConvention = CallingConvention.Cdecl)]
        public static extern lua_State* RuntimeRootState(IntPtr rt);

        [DllImport(DllName, EntryPoint = "basis_luau_runtime_release_root_ownership", CallingConvention = CallingConvention.Cdecl)]
        static extern void RuntimeReleaseRootOwnership(IntPtr rt);

        [DllImport(DllName, EntryPoint = "basis_luau_runtime_set_host_id", CallingConvention = CallingConvention.Cdecl)]
        static extern void RuntimeSetHostId(IntPtr rt, ushort hostId);

        [DllImport(DllName, EntryPoint = "basis_luau_runtime_set_host_kind", CallingConvention = CallingConvention.Cdecl)]
        static extern void RuntimeSetHostKind(IntPtr rt, int hostKind);

        [DllImport(DllName, EntryPoint = "basis_luau_ring_try_push_command", CallingConvention = CallingConvention.Cdecl)]
        public static extern BasisLuauRingResult TryPushCommand(IntPtr rt, uint hostId, ref BasisLuauCommandNative cmd);

        [DllImport(DllName, EntryPoint = "basis_luau_event_ring_try_push", CallingConvention = CallingConvention.Cdecl)]
        public static extern BasisLuauRingResult TryPushEvent(IntPtr rt, uint hostId, ref BasisLuauCommandNative cmd);

        [DllImport(DllName, EntryPoint = "basis_luau_ring_try_pop_command", CallingConvention = CallingConvention.Cdecl)]
        public static extern BasisLuauRingResult TryPopCommand(IntPtr rt, out BasisLuauCommandNative cmd);

        [DllImport(DllName, EntryPoint = "basis_luau_snapshot_publish_begin", CallingConvention = CallingConvention.Cdecl)]
        static extern int SnapshotPublishBegin(IntPtr rt, out ulong epoch);

        [DllImport(DllName, EntryPoint = "basis_luau_snapshot_publish_end", CallingConvention = CallingConvention.Cdecl)]
        static extern void SnapshotPublishEnd(IntPtr rt, ulong epoch);

        [DllImport(DllName, EntryPoint = "basis_luau_snapshot_write_slot", CallingConvention = CallingConvention.Cdecl)]
        static extern int SnapshotWriteSlot(IntPtr rt, uint slotIndex, ref BasisLuauSnapshotSlotNative slot);

        [DllImport(DllName, EntryPoint = "basis_luau_buffer_alloc", CallingConvention = CallingConvention.Cdecl)]
        static extern int BufferAlloc(IntPtr rt, uint hostId, uint length, out BasisLuauBufferRefNative bufferRef, out IntPtr bytes);

        [DllImport(DllName, EntryPoint = "basis_luau_buffer_get", CallingConvention = CallingConvention.Cdecl)]
        static extern int BufferGet(IntPtr rt, ref BasisLuauBufferRefNative bufferRef, out IntPtr bytes, out uint length);

        [DllImport(DllName, EntryPoint = "basis_luau_proxy_register", CallingConvention = CallingConvention.Cdecl)]
        static extern int ProxyRegister(
            IntPtr rt,
            uint proxyId,
            lua_State* thread,
            uint proxyGeneration,
            int refUpdate,
            int refFixedUpdate,
            int refLateUpdate,
            int refTriggerEnter,
            int refTriggerExit,
            int refCollisionEnter,
            int refCollisionExit);

        [DllImport(DllName, EntryPoint = "basis_luau_proxy_unregister", CallingConvention = CallingConvention.Cdecl)]
        static extern void ProxyUnregister(IntPtr rt, uint proxyId);

        [DllImport(DllName, EntryPoint = "basis_luau_proxy_set_generation", CallingConvention = CallingConvention.Cdecl)]
        static extern void ProxySetGeneration(IntPtr rt, uint proxyId, uint proxyGeneration);

        [DllImport(DllName, EntryPoint = "basis_luau_proxy_set_thread_context", CallingConvention = CallingConvention.Cdecl)]
        static extern void ProxySetThreadContext(lua_State* thread, uint proxyId, uint proxyGeneration);

        [DllImport(DllName, EntryPoint = "basis_luau_scheduler_start", CallingConvention = CallingConvention.Cdecl)]
        static extern int SchedulerStart(IntPtr rt, int workerCount);

        [DllImport(DllName, EntryPoint = "basis_luau_scheduler_shutdown", CallingConvention = CallingConvention.Cdecl)]
        static extern void SchedulerShutdown(IntPtr rt);

        [DllImport(DllName, EntryPoint = "basis_luau_buffer_release", CallingConvention = CallingConvention.Cdecl)]
        static extern void BufferRelease(IntPtr rt, ref BasisLuauBufferRefNative bufferRef);

        [DllImport(DllName, EntryPoint = "basis_luau_runtime_set_datetime_buffer", CallingConvention = CallingConvention.Cdecl)]
        static extern void RuntimeSetDatetimeBuffer(IntPtr rt, ref BasisLuauBufferRefNative bufferRef);

        [DllImport(DllName, EntryPoint = "basis_luau_begin_execution", CallingConvention = CallingConvention.Cdecl)]
        public static extern void BeginExecution(lua_State* L, long budgetNs);

        [DllImport(DllName, EntryPoint = "basis_luau_end_execution", CallingConvention = CallingConvention.Cdecl)]
        public static extern void EndExecution(lua_State* L);

        [DllImport(DllName, EntryPoint = "basis_luau_last_disable_reason", CallingConvention = CallingConvention.Cdecl)]
        public static extern BasisLuauDisableReason LastDisableReason(lua_State* L);

        IntPtr _handle;
        LuauExecutionLimits _limits;

        public IntPtr Handle => _handle;
        public bool IsCreated => _handle != IntPtr.Zero;
        public LuauExecutionLimits Limits => _limits;

        static bool? _isAvailable;
        static string _unavailableReason;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAvailability()
        {
            _isAvailable = null;
            _unavailableReason = null;
        }

        public static string UnavailableReason => _unavailableReason;

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue)
                {
                    return _isAvailable.Value;
                }

                try
                {
                    var cfg = new BasisLuauLimitsConfig { memory_cap_bytes = 8 * 1024 * 1024 };
                    basis_luau_init_error err;
                    IntPtr probe = RuntimeCreate(ref cfg, &err);
                    if (probe == IntPtr.Zero)
                    {
                        _unavailableReason = $"basis_luau_runtime_create failed: {err}";
                        _isAvailable = false;
                    }
                    else
                    {
                        RuntimeSetHostId(probe, 1);
                        RuntimeDestroy(probe);
                        _unavailableReason = null;
                        _isAvailable = true;
                    }
                }
                catch (DllNotFoundException ex)
                {
                    _unavailableReason = $"DLL not found: {ex.Message}";
                    _isAvailable = false;
                }
                catch (EntryPointNotFoundException ex)
                {
                    _unavailableReason = $"Entry point not found: {ex.Message}";
                    _isAvailable = false;
                }
                catch (Exception ex)
                {
                    _unavailableReason = ex.Message;
                    _isAvailable = false;
                }

#if UNITY_EDITOR
                if (!_isAvailable.Value && !string.IsNullOrEmpty(_unavailableReason))
                {
                    Debug.LogWarning($"[BasisLuau] Native runtime unavailable: {_unavailableReason}");
                }
#endif

                return _isAvailable.Value;
            }
        }

        public BasisLuauRingResult PushCommand(uint hostId, ref BasisLuauCommandNative cmd) =>
            TryPushCommand(_handle, hostId, ref cmd);

        public BasisLuauRingResult PushEvent(uint hostId, ref BasisLuauCommandNative cmd) =>
            TryPushEvent(_handle, hostId, ref cmd);

        public BasisLuauRingResult PopCommand(out BasisLuauCommandNative cmd) =>
            TryPopCommand(_handle, out cmd);

        public void KickScheduler(float deltaTime, float fixedDeltaTime, bool tickFixed) =>
            SchedulerKick(_handle, deltaTime, fixedDeltaTime, tickFixed ? 1 : 0);

        public void StartScheduler(int workerCount) => SchedulerStart(_handle, workerCount);

        public void Create(LuauExecutionLimits limits, ushort hostId, int hostKind)
        {
            if (_handle != IntPtr.Zero)
            {
                return;
            }

            _limits = limits;
            var config = new BasisLuauLimitsConfig
            {
                memory_cap_bytes = limits.MemoryBudgetBytes,
            };
            basis_luau_init_error err;
            _handle = RuntimeCreate(ref config, &err);
            if (_handle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"basis_luau_runtime_create failed: {err}");
            }

            RuntimeSetHostId(_handle, hostId);
            RuntimeSetHostKind(_handle, hostKind);
            limits.AttachRuntime(_handle, RuntimeRootState(_handle));
            RuntimeReleaseRootOwnership(_handle);
        }

        public LuauState CreateRootStateWrapper() => _limits?.RootState;

        [DllImport(DllName, EntryPoint = "basis_luau_scheduler_kick", CallingConvention = CallingConvention.Cdecl)]
        static extern void SchedulerKick(IntPtr rt, float deltaTime, float fixedDeltaTime, int tickFixed);

        public bool BeginSnapshotPublish(out ulong epoch) => SnapshotPublishBegin(_handle, out epoch) != 0;

        public void EndSnapshotPublish(ulong epoch) => SnapshotPublishEnd(_handle, epoch);

        public unsafe int TryGetBufferBytes(ref BasisLuauBufferRefNative bufferRef, out byte* bytes, out uint length)
        {
            bytes = null;
            length = 0;
            IntPtr ptr;
            uint len;
            int ok = BufferGet(_handle, ref bufferRef, out ptr, out len);
            if (ok == 0)
            {
                return 0;
            }

            bytes = (byte*)ptr;
            length = len;
            return 1;
        }

        public void WriteSnapshotSlot(uint slotIndex, ref BasisLuauSnapshotSlotNative slot) =>
            SnapshotWriteSlot(_handle, slotIndex, ref slot);

        public bool TryAllocBuffer(uint hostId, uint length, out BasisLuauBufferRefNative bufferRef, out IntPtr bytes) =>
            BufferAlloc(_handle, hostId, length, out bufferRef, out bytes) != 0;

        public void ReleaseBuffer(ref BasisLuauBufferRefNative bufferRef) =>
            BufferRelease(_handle, ref bufferRef);

        public void SetDatetimeBuffer(ref BasisLuauBufferRefNative bufferRef) =>
            RuntimeSetDatetimeBuffer(_handle, ref bufferRef);

        public bool RegisterProxy(
            uint proxyId,
            LuauState thread,
            uint proxyGeneration,
            int refUpdate,
            int refFixedUpdate,
            int refLateUpdate,
            int refTriggerEnter,
            int refTriggerExit,
            int refCollisionEnter,
            int refCollisionExit)
        {
            return ProxyRegister(
                _handle,
                proxyId,
                thread.AsPointer(),
                proxyGeneration,
                refUpdate,
                refFixedUpdate,
                refLateUpdate,
                refTriggerEnter,
                refTriggerExit,
                refCollisionEnter,
                refCollisionExit) != 0;
        }

        public void UnregisterProxy(uint proxyId) => ProxyUnregister(_handle, proxyId);

        public void SetProxyGeneration(uint proxyId, uint proxyGeneration) =>
            ProxySetGeneration(_handle, proxyId, proxyGeneration);

        public void SetThreadContext(LuauState thread, uint proxyId, uint proxyGeneration) =>
            ProxySetThreadContext(thread.AsPointer(), proxyId, proxyGeneration);

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                SchedulerShutdown(_handle);
                RuntimeDestroy(_handle);
                _handle = IntPtr.Zero;
            }

            _limits?.Dispose();
            _limits = null;
        }
    }
}
