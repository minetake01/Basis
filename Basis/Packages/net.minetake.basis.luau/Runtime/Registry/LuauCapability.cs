using System;
using UnityEngine;
using Minetake.Basis.Luau.Policy;

namespace Minetake.Basis.Luau.Registry
{
    public sealed class LuauCapability
    {
        readonly LuauHostKind _hostKind;
        readonly Transform _contentRoot;
        readonly LuauWhitelistPolicy _policy;

        public LuauCapability(LuauHostKind hostKind, Transform contentRoot, LuauWhitelistPolicy policy)
        {
            _hostKind = hostKind;
            _contentRoot = contentRoot;
            _policy = policy;
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

            if (_policy != null && !ValidateTypeAccess(obj.GetType()))
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

        public bool ValidateTypeAccess(Type type)
        {
            if (type == null || _policy == null)
            {
                return false;
            }

            return _policy.CheckTypeAllowed(type.FullName);
        }

        public bool ValidateHostKind(LuauHostKind expected) => _hostKind == expected;
    }
}
