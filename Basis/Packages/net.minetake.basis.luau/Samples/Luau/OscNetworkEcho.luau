return {
    start = function()
        basis_osc.subscribe("/avatar/parameters/Test", function(message)
            basis_util.log("OSC: " .. tostring(message.address))
        end)
    end,
    update = function(dt)
        basis_osc.publishFloat("Test/Spin", dt)
    end,
}
