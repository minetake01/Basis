namespace Minetake.Basis.Luau.Runtime
{
    public enum LuauFailureScope
    {
        None = 0,
        Proxy = 1,
        Host = 2,
    }

    public enum LuauFailureReason
    {
        None = 0,
        Timeout = 1,
        AllocFailure = 2,
        Panic = 3,
        Internal = 4,
        BytecodeRejected = 5,
        SignatureRejected = 6,
        CommandOverflow = 7,
        BufferQuota = 8,
        UnauthorizedHandle = 9,
        UnregisteredCommand = 10,
    }

    public static class LuauFailurePolicy
    {
        public static LuauFailureScope ScopeFor(LuauFailureReason reason) => reason switch
        {
            LuauFailureReason.Timeout or
            LuauFailureReason.BytecodeRejected or
            LuauFailureReason.SignatureRejected or
            LuauFailureReason.CommandOverflow => LuauFailureScope.Proxy,
            LuauFailureReason.AllocFailure or
            LuauFailureReason.Panic or
            LuauFailureReason.Internal or
            LuauFailureReason.BufferQuota => LuauFailureScope.Host,
            LuauFailureReason.UnauthorizedHandle or
            LuauFailureReason.UnregisteredCommand => LuauFailureScope.Proxy,
            _ => LuauFailureScope.None,
        };
    }
}
