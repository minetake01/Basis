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
        SetUiText = 6,
        Log = 7,
        Warn = 8,
        Error = 9,

        GetPositionSnapshot = 64,
        GetRotationSnapshot = 65,
        GetLocalPositionSnapshot = 66,

        TicketClone = 128,
        TicketDownloadImage = 129,
        TicketNetworkSend = 130,
        TicketTakeOwnership = 131,
        TicketMakeNetworkable = 132,
        TicketMakeInteractable = 133,
        TicketOscPublishFloat = 140,
        TicketOscPublishInt = 141,
        TicketOscPublishBool = 142,
        TicketOscPublishString = 143,
        TicketOscSubscribe = 144,
        TicketAvatarResolve = 150,
        TicketPlayerTeleport = 151,
        TicketPlayerRespawn = 152,
        TicketVixxyGet = 153,
        TicketVixxyApply = 154,
        TicketInteractPress = 155,

        EventNetworkMessage = 192,
        EventOscMessage = 193,
        EventTriggerEnter = 194,
        EventTriggerExit = 195,
        EventCollisionEnter = 196,
        EventCollisionExit = 197,

        Shutdown = 255,
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
            LuauCommandType.SetUiText or
            LuauCommandType.Log or
            LuauCommandType.Warn or
            LuauCommandType.Error or
            LuauCommandType.GetPositionSnapshot or
            LuauCommandType.GetRotationSnapshot or
            LuauCommandType.GetLocalPositionSnapshot or
            LuauCommandType.TicketClone or
            LuauCommandType.TicketDownloadImage or
            LuauCommandType.TicketNetworkSend or
            LuauCommandType.TicketTakeOwnership or
            LuauCommandType.TicketMakeNetworkable or
            LuauCommandType.TicketMakeInteractable or
            LuauCommandType.TicketOscPublishFloat or
            LuauCommandType.TicketOscPublishInt or
            LuauCommandType.TicketOscPublishBool or
            LuauCommandType.TicketOscPublishString or
            LuauCommandType.TicketOscSubscribe or
            LuauCommandType.TicketAvatarResolve or
            LuauCommandType.TicketPlayerTeleport or
            LuauCommandType.TicketPlayerRespawn or
            LuauCommandType.TicketVixxyGet or
            LuauCommandType.TicketVixxyApply or
            LuauCommandType.TicketInteractPress or
            LuauCommandType.EventNetworkMessage or
            LuauCommandType.EventOscMessage or
            LuauCommandType.EventTriggerEnter or
            LuauCommandType.EventTriggerExit or
            LuauCommandType.EventCollisionEnter or
            LuauCommandType.EventCollisionExit or
            LuauCommandType.Shutdown => true,
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
            LuauCommandType.TicketClone or
            LuauCommandType.TicketDownloadImage or
            LuauCommandType.TicketNetworkSend or
            LuauCommandType.TicketTakeOwnership or
            LuauCommandType.TicketMakeNetworkable or
            LuauCommandType.TicketMakeInteractable or
            LuauCommandType.TicketOscPublishFloat or
            LuauCommandType.TicketOscPublishInt or
            LuauCommandType.TicketOscPublishBool or
            LuauCommandType.TicketOscPublishString or
            LuauCommandType.TicketOscSubscribe or
            LuauCommandType.TicketAvatarResolve or
            LuauCommandType.TicketPlayerTeleport or
            LuauCommandType.TicketPlayerRespawn or
            LuauCommandType.TicketVixxyGet or
            LuauCommandType.TicketVixxyApply or
            LuauCommandType.TicketInteractPress => true,
            _ => false,
        };
    }
}
