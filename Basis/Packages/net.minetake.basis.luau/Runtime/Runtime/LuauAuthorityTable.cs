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
        public struct Entry
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

        public LuauHostKind HostKind => _hostKind;

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

        public bool TryResolve(LuauObjectHandle handle, out UnityEngine.Object target)
        {
            target = null;
            if (!handle.IsValid)
            {
                return false;
            }

            if (!_entries.TryGetValue(handle.Index, out Entry entry))
            {
                return false;
            }

            if (entry.Generation != handle.Generation || entry.Tombstoned)
            {
                return false;
            }

            if (entry.Target == null)
            {
                return false;
            }

            target = entry.Target;
            return true;
        }

        public bool TryResolve<T>(LuauObjectHandle handle, out T target) where T : UnityEngine.Object
        {
            target = null;
            if (!TryResolve(handle, out UnityEngine.Object obj))
            {
                return false;
            }

            target = obj as T;
            return target != null;
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

        public bool ValidateHandle(LuauObjectHandle handle, Type requiredType, out UnityEngine.Object target)
        {
            return TryValidate(handle.Index, handle.Generation, requiredType, out target);
        }

        public IEnumerable<KeyValuePair<uint, Entry>> EnumerateEntries() => _entries;

        public void BumpHostEpoch()
        {
            _hostEpoch++;
            _entries.Clear();
            _nextIndex = 1;
        }

        public void Clear()
        {
            _entries.Clear();
            _nextIndex = 1;
            _hostEpoch++;
        }
    }
}
