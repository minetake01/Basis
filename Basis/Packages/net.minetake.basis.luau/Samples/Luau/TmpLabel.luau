-- Assign handle slot 1 to a TMP_Text or TextMeshProUGUI component.
local LABEL = 0

return {
    start = function()
        if __luau_handles and __luau_handles[1] then
            LABEL = __luau_handles[1]
        end
    end,
    update = function(dt)
        if LABEL ~= 0 then
            basis_object.setField(LABEL, "text", string.format("Luau %.2f", dt))
        end
    end,
}
