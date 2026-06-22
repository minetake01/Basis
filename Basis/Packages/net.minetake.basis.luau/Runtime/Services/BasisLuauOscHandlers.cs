using HVR.Basis.Comms.OSC;
using Minetake.Basis.Luau.Runtime;
using UnityEngine;

namespace Minetake.Basis.Luau.Services
{
    public static class BasisLuauOscHandlers
    {
        public static BasisLuauOscHost GetOrCreateHost(LuauHostBase host)
        {
            if (host == null)
            {
                return null;
            }

            return host.GetComponent<BasisLuauOscHost>() ?? host.gameObject.AddComponent<BasisLuauOscHost>();
        }

        public static void PublishFloat(LuauHostBase host, string address, float value) =>
            GetOrCreateHost(host)?.PublishValue(address, OscData.Float32(value));

        public static void PublishInt(LuauHostBase host, string address, int value) =>
            GetOrCreateHost(host)?.PublishValue(address, OscData.Int32(value));

        public static void PublishBool(LuauHostBase host, string address, bool value) =>
            GetOrCreateHost(host)?.PublishValue(address, OscData.Boolean(value));

        public static void PublishString(LuauHostBase host, string address, string value) =>
            GetOrCreateHost(host)?.PublishValue(address, OscData.String(value ?? string.Empty));
    }
}
