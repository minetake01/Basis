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

        public bool ValidateHandle(LuauObjectRegistry registry, LuauObjectHandle handle, Type requiredType, out UnityEngine.Object target)
        {
            target = null;
            if (registry == null || !handle.IsValid)
            {
                return false;
            }

            if (!registry.TryResolve(handle, out UnityEngine.Object obj))
            {
                return false;
            }

            if (requiredType != null && !requiredType.IsInstanceOfType(obj))
            {
                return false;
            }

            if (_contentRoot != null && obj is Component component)
            {
                if (!component.transform.IsChildOf(_contentRoot))
                {
                    return false;
                }
            }

            target = obj;
            return true;
        }

        public bool ValidateHostKind(LuauHostKind expected) => _hostKind == expected;
    }
}
