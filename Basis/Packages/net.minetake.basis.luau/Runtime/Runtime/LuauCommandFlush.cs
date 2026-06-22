using System.Collections.Generic;
using Luau.Unity;
using Minetake.Basis.Luau;
using Minetake.Basis.Luau.Runtime;
using Minetake.Basis.Luau.Services;
using UnityEngine;

namespace Minetake.Basis.Luau.Runtime
{
    public static class LuauCommandFlush
    {
        public static void Apply(
            LuauHostBase host,
            LuauAuthorityTable authority,
            BasisLuauNativeRuntime native,
            LuauTicketProcessor tickets,
            Dictionary<uint, LuauScriptProxy.ProxyGenerationState> proxyGenerations,
            BasisLuauCommandNative cmd)
        {
            if (!LuauCommandCatalog.IsRegistered((LuauCommandType)cmd.Type))
            {
                return;
            }

            if (cmd.HostId != authority.HostId)
            {
                return;
            }

            if (cmd.ProxyId != 0 && proxyGenerations.TryGetValue(cmd.ProxyId, out LuauScriptProxy.ProxyGenerationState gen))
            {
                if (gen.Generation != cmd.ProxyGeneration || gen.Disabled)
                {
                    return;
                }
            }

            if (LuauCommandCatalog.IsTicket((LuauCommandType)cmd.Type))
            {
                tickets.ProcessCommand(cmd);
                return;
            }

            unsafe
            {
                float d0 = cmd.Data[0];
                float d1 = cmd.Data[1];
                float d2 = cmd.Data[2];
                float d3 = cmd.Data[3];

                switch ((LuauCommandType)cmd.Type)
                {
                    case LuauCommandType.SetPosition:
                        if (TryGetTransform(authority, cmd, out Transform t1))
                        {
                            t1.position = new Vector3(d0, d1, d2);
                        }
                        break;
                    case LuauCommandType.SetRotation:
                        if (TryGetTransform(authority, cmd, out Transform t2))
                        {
                            t2.rotation = Quaternion.Euler(d0, d1, d2);
                        }
                        break;
                    case LuauCommandType.SetLocalPosition:
                        if (TryGetTransform(authority, cmd, out Transform t3))
                        {
                            t3.localPosition = new Vector3(d0, d1, d2);
                        }
                        break;
                    case LuauCommandType.Rotate:
                        if (TryGetTransform(authority, cmd, out Transform t4))
                        {
                            Space space = d3 > 0.5f ? Space.Self : Space.World;
                            t4.Rotate(d0, d1, d2, space);
                        }
                        break;
                    case LuauCommandType.DestroyObject:
                        LuauServiceHandlers.Destroy(authority, cmd);
                        break;
                    case LuauCommandType.SetUiText:
                        LuauServiceHandlers.SetUiText(host, authority, native, cmd);
                        break;
                    case LuauCommandType.Log:
                        LuauServiceHandlers.Log(native, cmd);
                        break;
                    case LuauCommandType.Warn:
                        LuauServiceHandlers.Warn(native, cmd);
                        break;
                    case LuauCommandType.Error:
                        LuauServiceHandlers.Error(native, cmd);
                        break;
                }
            }
        }

        static bool TryGetTransform(LuauAuthorityTable authority, BasisLuauCommandNative cmd, out Transform transform)
        {
            transform = null;
            if (!authority.TryValidate(cmd.HandleIndex, cmd.HandleGeneration, typeof(Transform), out Object obj))
            {
                return false;
            }

            transform = obj as Transform;
            return transform != null;
        }
    }
}
