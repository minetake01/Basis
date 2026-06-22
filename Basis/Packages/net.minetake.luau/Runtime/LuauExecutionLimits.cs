using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
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

    public readonly struct ProtectedInvokeResult
    {
        public ProtectedCallResult Call { get; init; }
        public LuauValue[] ReturnValues { get; init; }
    }

    public static class LuauFunctionInvoke
    {
        public static LuauValue[] InvokeWithResults(LuauFunction function, ReadOnlySpan<LuauValue> args)
        {
            if (function == null)
            {
                return null;
            }

            LuauState state = function.State;
            state.Push(function);
            for (int i = 0; i < args.Length; i++)
            {
                state.Push(args[i]);
            }

            int nResults = function.InvokeAsync(args.Length, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            if (nResults <= 0)
            {
                return Array.Empty<LuauValue>();
            }

            var results = new LuauValue[nResults];
            for (int i = nResults - 1; i >= 0; i--)
            {
                results[i] = state.Pop();
            }

            return results;
        }
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

        IntPtr _runtimeHandle;
        LuauState _rootState;

        public TimeSpan ExecutionBudget { get; set; } = TimeSpan.FromMilliseconds(500);
        public nuint MemoryBudgetBytes { get; set; } = 8 * 1024 * 1024;
        public LuauState RootState => _rootState;

        public void AttachRuntime(IntPtr runtimeHandle, lua_State* rootPtr)
        {
            _runtimeHandle = runtimeHandle;
            if (CreateStateInternal == null)
            {
                throw new InvalidOperationException("LuauState.CreateStateInternal reflection failed.");
            }

            _rootState = ((CreateStateInternalDelegate)Delegate.CreateDelegate(typeof(CreateStateInternalDelegate), CreateStateInternal))(rootPtr);
        }

        public ProtectedCallResult InvokeProtected(LuauState state, LuauFunction function, ReadOnlySpan<LuauValue> args) =>
            InvokeProtectedWithResults(state, function, args).Call;

        public void BeginProtectedLoad(LuauState state)
        {
            if (state == null)
            {
                return;
            }

            BasisLuauNativeRuntime.BeginExecution(state.AsPointer(), GetValidatedBudgetNs());
        }

        public void EndProtectedLoad(LuauState state)
        {
            if (state == null)
            {
                return;
            }

            BasisLuauNativeRuntime.EndExecution(state.AsPointer());
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
            BasisLuauNativeRuntime.BeginExecution(L, budgetNs);

            try
            {
                LuauValue[] results = LuauFunctionInvoke.InvokeWithResults(function, args);
                BasisLuauNativeRuntime.EndExecution(L);
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
                BasisLuauNativeRuntime.EndExecution(L);
                LuauDisableReason reason = MapReason(BasisLuauNativeRuntime.LastDisableReason(L), ex);
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
            BasisLuauNativeRuntime.BeginExecution(L, budgetNs);

            try
            {
                state.DoString(sourceUtf8);
                BasisLuauNativeRuntime.EndExecution(L);
                return new ProtectedCallResult
                {
                    Success = true,
                    Reason = LuauDisableReason.None,
                    RecoveredViaProtectedCall = true,
                };
            }
            catch (Exception ex)
            {
                BasisLuauNativeRuntime.EndExecution(L);
                LuauDisableReason reason = MapReason(BasisLuauNativeRuntime.LastDisableReason(L), ex);
                return new ProtectedCallResult
                {
                    Success = false,
                    Reason = reason,
                    ErrorMessage = ex.Message,
                    RecoveredViaProtectedCall = true,
                };
            }
        }

        public void Dispose()
        {
            _rootState?.Dispose();
            _rootState = null;
            _runtimeHandle = IntPtr.Zero;
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

    public enum BasisLuauDisableReason
    {
        None = 0,
        Timeout = 1,
        AllocFailure = 2,
        Panic = 3,
        Internal = 4,
    }
}
