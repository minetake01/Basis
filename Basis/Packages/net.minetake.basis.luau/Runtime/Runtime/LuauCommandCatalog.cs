namespace Minetake.Basis.Luau.Runtime
{
    /// <summary>
    /// Deny-by-default command IDs. Must match native <c>basis_luau_command_type</c>.
    /// </summary>
    public enum LuauCommandType : ushort
    {
        Invalid = 0,

        SetPosition = 1,
        SetRotation = 2,
        SetLocalPosition = 3,
        Rotate = 4,
        DestroyObject = 5,

        GetPositionSnapshot = 64,
        GetRotationSnapshot = 65,

        TicketClone = 128,
        TicketDownloadImage = 129,

        EventNetworkMessage = 192,
        EventOscMessage = 193,
        EventTriggerEnter = 194,
    }

    public static class LuauCommandCatalog
    {
        public static bool IsRegistered(LuauCommandType type) => type switch
        {
            LuauCommandType.SetPosition or
            LuauCommandType.SetRotation or
            LuauCommandType.SetLocalPosition or
            LuauCommandType.Rotate or
            LuauCommandType.DestroyObject or
            LuauCommandType.GetPositionSnapshot or
            LuauCommandType.GetRotationSnapshot or
            LuauCommandType.TicketClone or
            LuauCommandType.TicketDownloadImage or
            LuauCommandType.EventNetworkMessage or
            LuauCommandType.EventOscMessage or
            LuauCommandType.EventTriggerEnter => true,
            _ => false,
        };

        public static bool IsDeferredWrite(LuauCommandType type) => type switch
        {
            LuauCommandType.SetPosition or
            LuauCommandType.SetRotation or
            LuauCommandType.SetLocalPosition or
            LuauCommandType.Rotate => true,
            _ => false,
        };

        public static bool IsTicket(LuauCommandType type) => type switch
        {
            LuauCommandType.TicketClone or LuauCommandType.TicketDownloadImage => true,
            _ => false,
        };
    }
}
