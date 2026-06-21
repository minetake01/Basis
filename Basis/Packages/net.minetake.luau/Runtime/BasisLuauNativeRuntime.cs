using System;
using System.Runtime.InteropServices;
using Luau.Unity;

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

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 32)]
    public struct BasisLuauCommandNative
    {
        public ushort Type;
        public ushort HostId;
        public uint HandleIndex;
        public uint HandleGeneration;
        public uint ProxyGeneration;
        public uint Sequence;
        public unsafe fixed float Data[4];
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

        [DllImport(DllName, EntryPoint = "basis_luau_ring_try_push_command", CallingConvention = CallingConvention.Cdecl)]
        public static extern BasisLuauRingResult TryPushCommand(IntPtr rt, uint hostId, ref BasisLuauCommandNative cmd);

        [DllImport(DllName, EntryPoint = "basis_luau_ring_try_pop_command", CallingConvention = CallingConvention.Cdecl)]
        public static extern BasisLuauRingResult TryPopCommand(IntPtr rt, out BasisLuauCommandNative cmd);

        [DllImport(DllName, EntryPoint = "basis_luau_scheduler_start", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SchedulerStart(IntPtr rt, int workerCount);

        [DllImport(DllName, EntryPoint = "basis_luau_scheduler_shutdown", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SchedulerShutdown(IntPtr rt);

        [DllImport(DllName, EntryPoint = "basis_luau_scheduler_kick", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SchedulerKick(IntPtr rt);

        IntPtr _handle;

        public IntPtr Handle => _handle;
        public bool IsCreated => _handle != IntPtr.Zero;

        static bool? _isAvailable;

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
                    var cfg = new BasisLuauLimitsConfig { memory_cap_bytes = 1024 * 1024 };
                    basis_luau_init_error err;
                    IntPtr probe = RuntimeCreate(ref cfg, &err);
                    if (probe == IntPtr.Zero)
                    {
                        _isAvailable = false;
                    }
                    else
                    {
                        RuntimeDestroy(probe);
                        _isAvailable = true;
                    }
                }
                catch (DllNotFoundException)
                {
                    _isAvailable = false;
                }
                catch (EntryPointNotFoundException)
                {
                    _isAvailable = false;
                }

                return _isAvailable.Value;
            }
        }

        public BasisLuauRingResult PushCommand(uint hostId, ref BasisLuauCommandNative cmd) =>
            TryPushCommand(_handle, hostId, ref cmd);

        public BasisLuauRingResult PopCommand(out BasisLuauCommandNative cmd) =>
            TryPopCommand(_handle, out cmd);

        public void KickScheduler() => SchedulerKick(_handle);

        public void StartScheduler(int workerCount) => SchedulerStart(_handle, workerCount);

        public void Create(LuauExecutionLimits limits)
        {
            if (_handle != IntPtr.Zero)
            {
                return;
            }

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
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                SchedulerShutdown(_handle);
                RuntimeDestroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
