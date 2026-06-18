-- OscEcho.luau
-- Publishes a float OSC parameter when update runs.

return {
    update = function(dt: number)
        basis_osc.publishFloat("Test/Spin", dt)
    end,
}
