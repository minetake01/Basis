using System;
using System.Collections.Generic;
using UnityEngine;

namespace Minetake.Basis.Luau.Registry
{
    /// <summary>
    /// Per-host object registry. Must be owned as an instance field on <see cref="Hosts.LuauHostBase"/> only.
    /// </summary>
    public sealed class LuauObjectRegistry
    {
        sealed class Entry
        {
            public uint Generation;
            public UnityEngine.Object Target;
        }

        readonly Dictionary<uint, Entry> _entries = new();
        uint _nextIndex = 1;
        uint _currentGeneration = 1;
        bool _disposed;

        public LuauObjectHandle Register(UnityEngine.Object target)
        {
            ThrowIfDisposed();
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            uint index = _nextIndex++;
            var entry = new Entry
            {
                Generation = _currentGeneration,
                Target = target,
            };
            _entries[index] = entry;
            return LuauObjectHandle.Create(_currentGeneration, index);
        }

        public bool TryResolve(LuauObjectHandle handle, out UnityEngine.Object target)
        {
            target = null;
            if (_disposed || !handle.IsValid)
            {
                return false;
            }

            if (!_entries.TryGetValue(handle.Index, out Entry entry))
            {
                return false;
            }

            if (entry.Generation != handle.Generation)
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

        public void Release(LuauObjectHandle handle)
        {
            if (_disposed || !handle.IsValid)
            {
                return;
            }

            if (_entries.TryGetValue(handle.Index, out Entry entry) && entry.Generation == handle.Generation)
            {
                _entries.Remove(handle.Index);
            }
        }

        public void Clear()
        {
            _entries.Clear();
            _nextIndex = 1;
            _currentGeneration++;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _entries.Clear();
            _nextIndex = 1;
            _currentGeneration++;
        }

        void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LuauObjectRegistry));
            }
        }
    }
}
