using System;

namespace Minetake.Basis.Luau.Registry
{
    /// <summary>
    /// Opaque generation-index handle. Index is never reused while the owning host lives.
    /// </summary>
    public readonly struct LuauObjectHandle : IEquatable<LuauObjectHandle>
    {
        public const ulong InvalidRaw = 0UL;

        readonly ulong _raw;

        LuauObjectHandle(ulong raw) => _raw = raw;

        public bool IsValid => _raw != InvalidRaw;

        public uint Index => (uint)(_raw & 0xFFFFFFFFUL);

        public uint Generation => (uint)(_raw >> 32);

        internal static LuauObjectHandle Create(uint generation, uint index)
        {
            if (index == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "handle index must be non-zero");
            }

            return new LuauObjectHandle(((ulong)generation << 32) | index);
        }

        public static LuauObjectHandle FromRaw(ulong raw) => new LuauObjectHandle(raw);

        public ulong ToRaw() => _raw;

        public bool Equals(LuauObjectHandle other) => _raw == other._raw;

        public override bool Equals(object obj) => obj is LuauObjectHandle other && Equals(other);

        public override int GetHashCode() => _raw.GetHashCode();

        public static bool operator ==(LuauObjectHandle left, LuauObjectHandle right) => left.Equals(right);

        public static bool operator !=(LuauObjectHandle left, LuauObjectHandle right) => !left.Equals(right);

        public override string ToString() => IsValid ? $"LuauHandle(g={Generation}, i={Index})" : "LuauHandle(invalid)";
    }
}
