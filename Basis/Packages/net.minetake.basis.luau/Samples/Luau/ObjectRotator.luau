local HANDLE = 0

return {
    start = function()
        if __luau_handles and __luau_handles[1] then
            HANDLE = __luau_handles[1]
        end
    end,
    update = function(dt)
        if HANDLE ~= 0 then
            transform.rotate(HANDLE, 0, 45 * dt, 0)
        end
    end,
}
