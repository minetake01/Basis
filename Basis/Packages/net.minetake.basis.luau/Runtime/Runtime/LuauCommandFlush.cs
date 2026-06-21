using Luau.Unity;
using Minetake.Basis.Luau;
using Minetake.Basis.Luau.Runtime;
using UnityEngine;

namespace Minetake.Basis.Luau.Runtime
{
    public static class LuauCommandFlush
    {
        public static void Apply(LuauHostBase host, LuauAuthorityTable authority, BasisLuauCommandNative cmd)
        {
            if (!LuauCommandCatalog.IsRegistered((LuauCommandType)cmd.Type))
            {
                return;
            }

            if (cmd.HostId != authority.HostId)
            {
                return;
            }

            if (!authority.TryValidate(cmd.HandleIndex, cmd.HandleGeneration, typeof(Transform), out Object obj))
            {
                return;
            }

            var transform = obj as Transform;
            if (transform == null)
            {
                return;
            }

            unsafe
            {
                switch ((LuauCommandType)cmd.Type)
                {
                    case LuauCommandType.SetPosition:
                        transform.position = new Vector3(cmd.Data[0], cmd.Data[1], cmd.Data[2]);
                        break;
                    case LuauCommandType.Rotate:
                        transform.Rotate(cmd.Data[0], cmd.Data[1], cmd.Data[2], Space.World);
                        break;
                    case LuauCommandType.SetLocalPosition:
                        transform.localPosition = new Vector3(cmd.Data[0], cmd.Data[1], cmd.Data[2]);
                        break;
                    case LuauCommandType.DestroyObject:
                        authority.Tombstone(cmd.HandleIndex, cmd.HandleGeneration);
                        Object.Destroy(transform.gameObject);
                        break;
                }
            }
        }
    }
}
