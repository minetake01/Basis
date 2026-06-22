using System.Collections.Generic;
using Luau.Unity;
using Minetake.Basis.Luau.Registry;
using Minetake.Basis.Luau.Services;
using UnityEngine;

namespace Minetake.Basis.Luau.Runtime
{
    public sealed class LuauTicketProcessor
    {
        readonly LuauHostBase _host;
        readonly LuauAuthorityTable _authority;
        BasisLuauNativeRuntime _native;
        readonly LuauTicketRegistry _tickets;
        readonly Dictionary<uint, LuauScriptProxy> _proxyById = new();

        public LuauTicketProcessor(
            LuauHostBase host,
            LuauAuthorityTable authority,
            LuauTicketRegistry tickets)
        {
            _host = host;
            _authority = authority;
            _tickets = tickets;
        }

        public void SetNative(BasisLuauNativeRuntime native) => _native = native;

        public void RegisterProxy(uint proxyId, LuauScriptProxy proxy) => _proxyById[proxyId] = proxy;

        public void UnregisterProxy(uint proxyId) => _proxyById.Remove(proxyId);

        public void ProcessCommand(BasisLuauCommandNative cmd)
        {
            if (!LuauCommandCatalog.IsTicket((LuauCommandType)cmd.Type))
            {
                return;
            }

            uint ticketId = _tickets.Issue(handle => { });
            _proxyById.TryGetValue(cmd.ProxyId, out LuauScriptProxy proxy);

            switch ((LuauCommandType)cmd.Type)
            {
                case LuauCommandType.TicketClone:
                    LuauServiceHandlers.Clone(_authority, _tickets, ticketId, cmd);
                    break;
                case LuauCommandType.TicketDownloadImage:
                    LuauServiceHandlers.DownloadImage(_host, proxy, _native, _tickets, ticketId, cmd);
                    break;
                case LuauCommandType.TicketTakeOwnership:
                    LuauServiceHandlers.TakeOwnership(_host, _authority, cmd);
                    break;
                case LuauCommandType.TicketMakeNetworkable:
                    LuauServiceHandlers.MakeNetworkable(_host, _authority, _tickets, ticketId, cmd);
                    break;
                case LuauCommandType.TicketMakeInteractable:
                    LuauServiceHandlers.MakeInteractable(_host, _authority, _tickets, ticketId, cmd);
                    break;
                case LuauCommandType.TicketOscPublishFloat:
                    LuauServiceHandlers.OscPublishFloat(_host, _native, cmd);
                    break;
                case LuauCommandType.TicketOscSubscribe:
                    LuauServiceHandlers.OscSubscribe(_host, proxy, _native, cmd);
                    break;
                case LuauCommandType.TicketAvatarResolve:
                    LuauServiceHandlers.AvatarResolve(_host, _authority, _tickets, ticketId, cmd);
                    break;
            }
        }

        public void ProcessPending()
        {
        }
    }
}
