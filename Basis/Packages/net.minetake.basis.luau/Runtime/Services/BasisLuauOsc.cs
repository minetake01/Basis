using System;
using System.Collections.Generic;
using HVR.Basis.Comms.OSC;
using Luau;
using Minetake.Basis.Luau.Bindings;
using OscMessageEvent = Minetake.Basis.Luau.Services.BasisLuauOscHost.OscMessageEvent;
using OscValueEvent = Minetake.Basis.Luau.Services.BasisLuauOscHost.OscValueEvent;

namespace Minetake.Basis.Luau.Services
{
    [LuauLibrary("basis_osc")]
    public partial class BasisLuauOsc
    {
        static readonly Dictionary<BasisLuauOscHost, List<Action>> Cleanup = new();

        public static void Install(LuauState state, LuauHostBase host)
        {
            state?.OpenLibrary<BasisLuauOsc>();
        }

        static BasisLuauOscHost GetHost()
        {
            var host = LuauBindingContext.Host;
            if (host == null)
            {
                return null;
            }

            var shim = host.GetComponent<BasisLuauOscHost>() ?? host.gameObject.AddComponent<BasisLuauOscHost>();
            return shim;
        }

        [LuauMember("publishFloat")]
        public static void PublishFloat(string address, double value) =>
            GetHost()?.PublishValue(address, OscData.Float32((float)value));

        [LuauMember("publishInt")]
        public static void PublishInt(string address, double value) =>
            GetHost()?.PublishValue(address, OscData.Int32((int)value));

        [LuauMember("publishBool")]
        public static void PublishBool(string address, bool value) =>
            GetHost()?.PublishValue(address, OscData.Boolean(value));

        [LuauMember("publishString")]
        public static void PublishString(string address, string value) =>
            GetHost()?.PublishValue(address, OscData.String(value ?? string.Empty));

        [LuauMember("subscribe")]
        public static void Subscribe(string address, LuauFunction callback, bool localOnly = false)
        {
            var osc = GetHost();
            if (osc == null || callback == null)
            {
                return;
            }

            OscMessageEvent handler = (message, _) => FireCallback(callback, OscLuauMarshaller.MessageToTable(message));
            osc.Subscribe(address, handler, localOnly);
        }

        [LuauMember("subscribeValue")]
        public static void SubscribeValue(string address, LuauFunction callback, bool localOnly = false)
        {
            var osc = GetHost();
            if (osc == null || callback == null)
            {
                return;
            }

            OscValueEvent handler = data => FireCallback(callback, OscLuauMarshaller.DataToTable(data));
            osc.SubscribeValue(address, handler, localOnly);
        }

        [LuauMember("subscribePrefix")]
        public static void SubscribePrefix(string prefix, LuauFunction callback)
        {
            var osc = GetHost();
            if (osc == null || callback == null)
            {
                return;
            }

            OscMessageEvent handler = (message, _) => FireCallback(callback, OscLuauMarshaller.MessageToTable(message));
            osc.SubscribePrefix(prefix, handler);
        }

        [LuauMember("clearSubscriptions")]
        public static void ClearSubscriptions() => GetHost()?.ClearSubscriptions();

        [LuauMember("setReceiveAll")]
        public static void SetReceiveAll(bool value)
        {
            var osc = GetHost();
            if (osc != null)
            {
                osc.ReceiveAll = value;
            }
        }

        static void FireCallback(LuauFunction callback, LuauValue table)
        {
            var host = LuauBindingContext.Host;
            var proxy = LuauBindingContext.Proxy;
            host?.InvokeCallback(proxy, callback, table);
        }
    }

    static class OscLuauMarshaller
    {
        public static LuauValue MessageToTable(OscMessage message)
        {
            var host = LuauBindingContext.Host;
            if (host == null || message == null)
            {
                return default;
            }

            LuauState thread = host.CreateSandboxedThread();
            LuauTable table = thread.CreateTable();
            table["address"] = message.Path ?? string.Empty;
            if (message.Arguments != null)
            {
                LuauTable args = thread.CreateTable();
                for (int i = 0; i < message.Arguments.Length; i++)
                {
                    args[i + 1] = DataToTable(message.Arguments[i]);
                }

                table["arguments"] = args;
            }

            return table;
        }

        public static LuauValue DataToTable(OscData data)
        {
            var host = LuauBindingContext.Host;
            if (host == null)
            {
                return default;
            }

            LuauState thread = host.CreateSandboxedThread();
            LuauTable table = thread.CreateTable();
            table["kind"] = data.Kind.ToString();
            switch (data.Kind)
            {
                case OscDataKind.Float32:
                    table["value"] = data.FloatValue;
                    break;
                case OscDataKind.Int32:
                    table["value"] = data.IntValue;
                    break;
                case OscDataKind.Boolean:
                    table["value"] = data.BoolValue;
                    break;
                case OscDataKind.String:
                    table["value"] = data.StringValue ?? string.Empty;
                    break;
            }

            return table;
        }
    }
}
