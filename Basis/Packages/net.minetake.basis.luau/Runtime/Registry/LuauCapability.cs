using System;
using UnityEngine;

namespace Minetake.Basis.Luau.Registry
{
    public sealed class LuauCapability
    {
        readonly LuauHostKind _hostKind;
        readonly Transform _contentRoot;

        public LuauCapability(LuauHostKind hostKind, Transform contentRoot)
        {
            _hostKind = hostKind;
            _contentRoot = contentRoot;
        }

        public bool ValidateHandle(Runtime.LuauAuthorityTable authority, LuauObjectHandle handle, Type requiredType, out UnityEngine.Object target)
        {
            target = null;
            if (authority == null || !handle.IsValid)
            {
                return false;
            }

            return authority.TryValidate(handle.Index, handle.Generation, requiredType, out target);
        }

        public bool ValidateHostKind(LuauHostKind expected) => _hostKind == expected;
    }
}
