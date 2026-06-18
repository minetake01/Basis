using Luau;
using Minetake.Basis.Luau.Bindings;
using HVR.Basis.Comms.OSC;

namespace Minetake.Basis.Luau.Services
{
    [LuauLibrary("basis_osc")]
    public partial class BasisLuauOsc
    {
        public static void Install(LuauState state, LuauHostBase host)
        {
            state?.OpenLibrary<BasisLuauOsc>();
        }

        [LuauMember("publishFloat")]
        public static void PublishFloat(string address, double value)
        {
            var host = LuauBindingContext.Host;
            if (host == null || string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            var shim = host.GetComponent<BasisLuauOscHost>() ?? host.gameObject.AddComponent<BasisLuauOscHost>();
            shim.PublishValue(address, OscData.Float32((float)value));
        }
    }
}
