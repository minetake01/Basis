using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Luau;
using Luau.Native;

namespace Luau.Unity
{
    public enum LuauDisableReason
    {
        None = 0,
        Timeout = 1,
        AllocFailure = 2,
        Panic = 3,
        Internal = 4,
    }

    public readonly struct ProtectedCallResult
    {
        public bool Success { get; init; }
        public LuauDisableReason Reason { get; init; }
        public string ErrorMessage { get; init; }
        public bool RecoveredViaProtectedCall { get; init; }
    }

    public sealed unsafe class LuauExecutionLimits : IDisposable
    {
        delegate LuauState CreateStateInternalDelegate(lua_State* ptr);

        static readonly MethodInfo CreateStateInternal = typeof(LuauState).GetMethod(
            "CreateStateInternal",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(lua_State*) },
            modifiers: null);

        public TimeSpan ExecutionBudget { get; set; } = TimeSpan.FromMilliseconds(500);
        public nuint MemoryBudgetBytes { get; set; } = 8 * 1024 * 1024;

        public LuauState CreateLimitedState()
        {
            var config = new BasisLuauLimitsConfig
            {
                memory_cap_bytes = MemoryBudgetBytes,
            };

            lua_State* ptr = BasisLuauNative.basis_luau_newstate_with_limits(ref config);
            if (ptr == null)
            {
                throw new InvalidOperationException("basis_luau_newstate_with_limits failed. Rebuild Native~/basis_luau_limits for this platform.");
            }

            if (CreateStateInternal == null)
            {
                BasisLuauNative.basis_luau_end_execution(ptr);
                throw new InvalidOperationException("LuauState.CreateStateInternal reflection failed.");
            }

            return ((CreateStateInternalDelegate)Delegate.CreateDelegate(typeof(CreateStateInternalDelegate), CreateStateInternal))(ptr);
        }

        public ProtectedCallResult InvokeProtected(LuauState state, LuauFunction function, ReadOnlySpan<LuauValue> args)
        {
            if (state == null || function == null)
            {
                return new ProtectedCallResult
                {
                    Success = false,
                    Reason = LuauDisableReason.Internal,
                    ErrorMessage = "state or function was null",
                    RecoveredViaProtectedCall = false,
                };
            }

            lua_State* L = state.AsPointer();
            long budgetNs = (long)(ExecutionBudget.TotalMilliseconds * 1_000_000.0);
            BasisLuauNative.basis_luau_begin_execution(L, budgetNs);

            try
            {
                LuauValue[] results = function.InvokeAsync(args.ToArray()).AsTask().GetAwaiter().GetResult();
                BasisLuauNative.basis_luau_end_execution(L);
                return new ProtectedCallResult
                {
                    Success = true,
                    Reason = LuauDisableReason.None,
                    RecoveredViaProtectedCall = true,
                };
            }
            catch (Exception ex)
            {
                BasisLuauNative.basis_luau_end_execution(L);
                LuauDisableReason reason = MapReason(BasisLuauNative.basis_luau_last_disable_reason(L), ex);
                return new ProtectedCallResult
                {
                    Success = false,
                    Reason = reason,
                    ErrorMessage = ex.Message,
                    RecoveredViaProtectedCall = true,
                };
            }
        }

        public ProtectedCallResult InvokeProtectedString(LuauState state, ReadOnlySpan<byte> sourceUtf8)
        {
            if (state == null)
            {
                return new ProtectedCallResult
                {
                    Success = false,
                    Reason = LuauDisableReason.Internal,
                    ErrorMessage = "state was null",
                    RecoveredViaProtectedCall = false,
                };
            }

            lua_State* L = state.AsPointer();
            long budgetNs = (long)(ExecutionBudget.TotalMilliseconds * 1_000_000.0);
            BasisLuauNative.basis_luau_begin_execution(L, budgetNs);

            try
            {
                state.DoString(sourceUtf8);
                BasisLuauNative.basis_luau_end_execution(L);
                return new ProtectedCallResult
                {
                    Success = true,
                    Reason = LuauDisableReason.None,
                    RecoveredViaProtectedCall = true,
                };
            }
            catch (Exception ex)
            {
                BasisLuauNative.basis_luau_end_execution(L);
                LuauDisableReason reason = MapReason(BasisLuauNative.basis_luau_last_disable_reason(L), ex);
                return new ProtectedCallResult
                {
                    Success = false,
                    Reason = reason,
                    ErrorMessage = ex.Message,
                    RecoveredViaProtectedCall = true,
                };
            }
        }

        public nuint GetTotalBytes(LuauState state)
        {
            if (state == null)
            {
                return 0;
            }

            return (nuint)BasisLuauNative.basis_luau_total_bytes(state.AsPointer());
        }

        public void Dispose()
        {
        }

        static LuauDisableReason MapReason(BasisLuauDisableReason native, Exception ex)
        {
            if (native == BasisLuauDisableReason.Timeout || ex.Message.Contains("execution time limit exceeded", StringComparison.Ordinal))
            {
                return LuauDisableReason.Timeout;
            }

            if (native == BasisLuauDisableReason.AllocFailure || ex.Message.Contains("memory", StringComparison.OrdinalIgnoreCase))
            {
                return LuauDisableReason.AllocFailure;
            }

            return native switch
            {
                BasisLuauDisableReason.Panic => LuauDisableReason.Panic,
                BasisLuauDisableReason.Internal => LuauDisableReason.Internal,
                _ => LuauDisableReason.Internal,
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BasisLuauLimitsConfig
    {
        public ulong memory_cap_bytes;
    }

    internal enum BasisLuauDisableReason
    {
        None = 0,
        Timeout = 1,
        AllocFailure = 2,
        Panic = 3,
        Internal = 4,
    }

    internal static unsafe class BasisLuauNative
    {
#if (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR
        const string DllName = "__Internal";
#else
        const string DllName = "basis_luau_limits";
#endif

        [DllImport(DllName, EntryPoint = "basis_luau_newstate_with_limits", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern lua_State* basis_luau_newstate_with_limits(ref BasisLuauLimitsConfig config);

        [DllImport(DllName, EntryPoint = "basis_luau_set_execution_deadline", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void basis_luau_set_execution_deadline(lua_State* L, long deadline_ns_monotonic);

        [DllImport(DllName, EntryPoint = "basis_luau_begin_execution", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void basis_luau_begin_execution(lua_State* L, long budget_ns);

        [DllImport(DllName, EntryPoint = "basis_luau_end_execution", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void basis_luau_end_execution(lua_State* L);

        [DllImport(DllName, EntryPoint = "basis_luau_total_bytes", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ulong basis_luau_total_bytes(lua_State* L);

        [DllImport(DllName, EntryPoint = "basis_luau_memory_cap", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ulong basis_luau_memory_cap(lua_State* L);

        [DllImport(DllName, EntryPoint = "basis_luau_last_disable_reason", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern BasisLuauDisableReason basis_luau_last_disable_reason(lua_State* L);
    }
}
