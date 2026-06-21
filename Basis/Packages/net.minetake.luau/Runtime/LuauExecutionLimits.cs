using System;
using System.Reflection;
using System.Runtime.InteropServices;
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

    public readonly struct ProtectedInvokeResult
    {
        public ProtectedCallResult Call { get; init; }
        public LuauValue[] ReturnValues { get; init; }
    }

    public sealed unsafe class LuauExecutionLimits : IDisposable
    {
        const long MaxBudgetNs = 3_600_000_000_000L;

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
            try
            {
                var config = new BasisLuauLimitsConfig
                {
                    memory_cap_bytes = MemoryBudgetBytes,
                };

                basis_luau_init_error initError = basis_luau_init_error.None;
                lua_State* ptr = BasisLuauNative.basis_luau_newstate_with_limits(ref config, &initError);
                if (ptr == null)
                {
                    throw CreateInitException(initError);
                }

                if (CreateStateInternal == null)
                {
                    throw new InvalidOperationException("LuauState.CreateStateInternal reflection failed.");
                }

                return ((CreateStateInternalDelegate)Delegate.CreateDelegate(typeof(CreateStateInternalDelegate), CreateStateInternal))(ptr);
            }
            catch (DllNotFoundException ex)
            {
                throw new InvalidOperationException(
                    "Failed to load basis_luau_limits or libluau. Rebuild Native~/build-libluau.ps1 for this platform.",
                    ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new InvalidOperationException(
                    "basis_luau_limits export mismatch. Rebuild Native~/build.ps1.",
                    ex);
            }
            catch (BadImageFormatException ex)
            {
                throw new InvalidOperationException(
                    "basis_luau_limits or libluau ABI/architecture mismatch (x64 required).",
                    ex);
            }
        }

        public ProtectedCallResult InvokeProtected(LuauState state, LuauFunction function, ReadOnlySpan<LuauValue> args)
        {
            return InvokeProtectedWithResults(state, function, args).Call;
        }

        public void BeginProtectedLoad(LuauState state)
        {
            if (state == null)
            {
                return;
            }

            long budgetNs = GetValidatedBudgetNs();
            BasisLuauNative.basis_luau_begin_execution(state.AsPointer(), budgetNs);
        }

        public void EndProtectedLoad(LuauState state)
        {
            if (state == null)
            {
                return;
            }

            BasisLuauNative.basis_luau_end_execution(state.AsPointer());
        }

        public ProtectedInvokeResult InvokeProtectedWithResults(LuauState state, LuauFunction function, ReadOnlySpan<LuauValue> args)
        {
            if (state == null || function == null)
            {
                return new ProtectedInvokeResult
                {
                    Call = new ProtectedCallResult
                    {
                        Success = false,
                        Reason = LuauDisableReason.Internal,
                        ErrorMessage = "state or function was null",
                        RecoveredViaProtectedCall = false,
                    },
                    ReturnValues = null,
                };
            }

            long budgetNs = GetValidatedBudgetNs();
            lua_State* L = state.AsPointer();
            BasisLuauNative.basis_luau_begin_execution(L, budgetNs);

            try
            {
                LuauValue[] results = function.InvokeAsync(args.ToArray()).AsTask().GetAwaiter().GetResult();
                BasisLuauNative.basis_luau_end_execution(L);
                return new ProtectedInvokeResult
                {
                    Call = new ProtectedCallResult
                    {
                        Success = true,
                        Reason = LuauDisableReason.None,
                        RecoveredViaProtectedCall = true,
                    },
                    ReturnValues = results,
                };
            }
            catch (Exception ex)
            {
                BasisLuauNative.basis_luau_end_execution(L);
                LuauDisableReason reason = MapReason(BasisLuauNative.basis_luau_last_disable_reason(L), ex);
                return new ProtectedInvokeResult
                {
                    Call = new ProtectedCallResult
                    {
                        Success = false,
                        Reason = reason,
                        ErrorMessage = ex.Message,
                        RecoveredViaProtectedCall = true,
                    },
                    ReturnValues = null,
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

            long budgetNs = GetValidatedBudgetNs();
            lua_State* L = state.AsPointer();
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

        long GetValidatedBudgetNs()
        {
            if (ExecutionBudget <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(ExecutionBudget), "ExecutionBudget must be positive.");
            }

            double budgetMs = ExecutionBudget.TotalMilliseconds;
            if (double.IsNaN(budgetMs) || double.IsInfinity(budgetMs))
            {
                throw new ArgumentOutOfRangeException(nameof(ExecutionBudget), "ExecutionBudget is not finite.");
            }

            long budgetNs = (long)(budgetMs * 1_000_000.0);
            if (budgetNs <= 0 || budgetNs > MaxBudgetNs)
            {
                throw new ArgumentOutOfRangeException(nameof(ExecutionBudget), "ExecutionBudget is out of supported range.");
            }

            return budgetNs;
        }

        static InvalidOperationException CreateInitException(basis_luau_init_error error) => error switch
        {
            basis_luau_init_error.InvalidConfig => new InvalidOperationException("basis_luau_newstate_with_limits: invalid config."),
            basis_luau_init_error.CtxAllocFailed => new InvalidOperationException("basis_luau_newstate_with_limits: context allocation failed."),
            basis_luau_init_error.VmAllocFailed => new InvalidOperationException("basis_luau_newstate_with_limits: VM allocation failed."),
            _ => new InvalidOperationException("basis_luau_newstate_with_limits failed. Rebuild Native~/build-libluau.ps1 for this platform."),
        };

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

    internal enum basis_luau_init_error
    {
        None = 0,
        InvalidConfig = 1,
        CtxAllocFailed = 2,
        VmAllocFailed = 3,
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
        public static extern lua_State* basis_luau_newstate_with_limits(ref BasisLuauLimitsConfig config, basis_luau_init_error* out_error);

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
