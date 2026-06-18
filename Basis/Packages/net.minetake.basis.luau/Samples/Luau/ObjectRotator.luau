-- ObjectRotator.luau
-- Slot 0 in BasisLuauBehaviour.handleSlots must reference the Transform to rotate.

local HANDLE: number = 0

return {
    start = function()
        local handles = __luau_handles
        if handles and handles[1] then
            HANDLE = handles[1]
        end
    end,
    update = function(dt: number)
        if HANDLE ~= 0 then
            transform.rotate(HANDLE, 0, 45 * dt, 0)
        end
    end,
}
