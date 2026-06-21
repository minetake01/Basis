using System;
using System.Collections.Generic;
using Luau.Unity;
using Minetake.Basis.Luau.Registry;
using UnityEngine;

namespace Minetake.Basis.Luau.Runtime
{
    /// <summary>
    /// Main-thread authority table — sole source of truth for handle validation at flush.
    /// </summary>
    public sealed class LuauAuthorityTable
    {
        struct Entry
        {
            public uint Generation;
            public UnityEngine.Object Target;
            public bool Tombstoned;
        }

        readonly Dictionary<uint, Entry> _entries = new();
        readonly Transform _contentRoot;
        readonly LuauHostKind _hostKind;
        uint _nextIndex = 1;
        uint _hostEpoch = 1;

        public uint HostId { get; }
        public uint HostEpoch => _hostEpoch;

        public LuauAuthorityTable(uint hostId, Transform contentRoot, LuauHostKind hostKind)
        {
            HostId = hostId;
            _contentRoot = contentRoot;
            _hostKind = hostKind;
        }

        public LuauObjectHandle Register(UnityEngine.Object target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            uint index = _nextIndex++;
            _entries[index] = new Entry
            {
                Generation = _hostEpoch,
                Target = target,
                Tombstoned = false,
            };
            return LuauObjectHandle.Create(_hostEpoch, index);
        }

        public void Tombstone(uint index, uint generation)
        {
            if (_entries.TryGetValue(index, out Entry entry) && entry.Generation == generation)
            {
                entry.Tombstoned = true;
                _entries[index] = entry;
            }
        }

        public bool TryValidate(uint index, uint generation, Type requiredType, out UnityEngine.Object target)
        {
            target = null;
            if (!_entries.TryGetValue(index, out Entry entry))
            {
                return false;
            }

            if (entry.Generation != generation || entry.Tombstoned)
            {
                return false;
            }

            if (entry.Target == null)
            {
                return false;
            }

            if (requiredType != null && !requiredType.IsInstanceOfType(entry.Target))
            {
                return false;
            }

            if (_contentRoot != null && entry.Target is Component component)
            {
                if (!component.transform.IsChildOf(_contentRoot))
                {
                    return false;
                }
            }

            target = entry.Target;
            return true;
        }

        public void BumpHostEpoch()
        {
            _hostEpoch++;
            _entries.Clear();
            _nextIndex = 1;
        }
    }
}
