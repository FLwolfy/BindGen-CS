namespace Generator.Caching
{
    using BGCS.Utils;
    using System;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Defines values for <c>CacheFileVersion</c>.
    /// </summary>
    public enum CacheFileVersion : uint
    {
        Version1 = 1 << 24 | 0 << 16 | 0 << 8 | 0
    }

    /// <summary>
    /// Defines the public struct <c>CacheFileHeader</c>.
    /// </summary>
    public struct CacheFileHeader
    {
        /// <summary>
        /// Exposes public member <c>0x00]</c>.
        /// </summary>
        public static readonly byte[] Magic = [0x48, 0x65, 0x78, 0x61, 0x47, 0x65, 0x6e, 0x43, 0x61, 0x63, 0x68, 0x65, 0x46, 0x69, 0x6c, 0x65, 0x00];
        /// <summary>
        /// Exposes public member <c>Version</c>.
        /// </summary>
        public CacheFileVersion Version;
        /// <summary>
        /// Exposes public member <c>Crc32</c>.
        /// </summary>
        public uint Crc32;
    }

    /// <summary>
    /// Defines the public struct <c>Hash256</c>.
    /// </summary>
    public struct Hash256 : IEquatable<Hash256>
    {
        /// <summary>
        /// Exposes public member <c>Value0</c>.
        /// </summary>
        public ulong Value0;
        /// <summary>
        /// Exposes public member <c>Value1</c>.
        /// </summary>
        public ulong Value1;
        /// <summary>
        /// Exposes public member <c>Value2</c>.
        /// </summary>
        public ulong Value2;
        /// <summary>
        /// Exposes public member <c>Value3</c>.
        /// </summary>
        public ulong Value3;

        /// <summary>
        /// Executes public operation <c>AsSpan</c>.
        /// </summary>
        public Span<byte> AsSpan() => MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref Value0, 4));

        /// <summary>
        /// Executes public operation <c>Span</c>.
        /// </summary>
        public static implicit operator Span<byte>(in Hash256 hash) => hash.AsSpan();

        /// <summary>
        /// Executes public operation <c>Equals</c>.
        /// </summary>
        public override readonly bool Equals(object? obj)
        {
            return obj is Hash256 hash && Equals(hash);
        }

        /// <summary>
        /// Executes public operation <c>Equals</c>.
        /// </summary>
        public readonly bool Equals(Hash256 other)
        {
            return Value0 == other.Value0 &&
                   Value1 == other.Value1 &&
                   Value2 == other.Value2 &&
                   Value3 == other.Value3;
        }

        /// <summary>
        /// Returns computed data from <c>GetHashCode</c>.
        /// </summary>
        public override readonly int GetHashCode()
        {
            return HashCode.Combine(Value0, Value1, Value2, Value3);
        }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator ==(Hash256 left, Hash256 right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator !=(Hash256 left, Hash256 right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Executes public operation <c>HashData</c>.
        /// </summary>
        public static Hash256 HashData(Stream stream)
        {
            Hash256 hash = default;
            SHA256.HashData(stream, hash);
            return hash;
        }
    }

    [InlineArray(MaxFileNameSize)]
    /// <summary>
    /// Defines the public struct <c>CacheFileName</c>.
    /// </summary>
    public struct CacheFileName : IEquatable<CacheFileName>, IEquatable<string>
    {
        /// <summary>
        /// Exposes public member <c>256</c>.
        /// </summary>
        public const int MaxFileNameSize = 256;
        byte byte0;

        /// <summary>
        /// Initializes a new instance of <see cref="CacheFileName"/>.
        /// </summary>
        public CacheFileName(string str)
        {
            if (Encoding.UTF8.GetByteCount(str) > MaxFileNameSize - 1) throw new ArgumentException("The file name is too long.", nameof(str));
            var span = AsSpan();
            int idx = Encoding.UTF8.GetBytes(str, span);
            span[Math.Min(idx, MaxFileNameSize - 1)] = 0;
        }

        /// <summary>
        /// Executes public operation <c>AsSpan</c>.
        /// </summary>
        public Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref byte0, MaxFileNameSize);

        /// <summary>
        /// Executes public operation <c>Equals</c>.
        /// </summary>
        public override bool Equals(object? obj)
        {
            return obj is CacheFileName name && Equals(name);
        }

        /// <summary>
        /// Executes public operation <c>Equals</c>.
        /// </summary>
        public bool Equals(CacheFileName other)
        {
            return AsSpan().SequenceEqual(other.AsSpan());
        }

        /// <summary>
        /// Executes public operation <c>Equals</c>.
        /// </summary>
        public bool Equals(string? other)
        {
            if (other is null) return false;
            Span<byte> buffer = stackalloc byte[MaxFileNameSize];
            int idx = Encoding.UTF8.GetBytes(other, buffer);
            buffer[Math.Min(idx, MaxFileNameSize - 1)] = 0;
            return AsSpan().SequenceEqual(buffer);
        }

        /// <summary>
        /// Returns computed data from <c>GetHashCode</c>.
        /// </summary>
        public override int GetHashCode() => (int)MurmurHash3.Hash32(AsSpan());

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator ==(CacheFileName left, CacheFileName right) => left.Equals(right);

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator !=(CacheFileName left, CacheFileName right) => !(left == right);

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator ==(CacheFileName left, string right) => left.Equals(right);

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator !=(CacheFileName left, string right) => !(left == right);

        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override unsafe string ToString()
        {
            fixed (byte* p = AsSpan())
            {
                return MemoryMarshal.CreateReadOnlySpanFromNullTerminated(p).ToString();
            }
        }
    }

    /// <summary>
    /// Defines the public struct <c>CacheFileEntry</c>.
    /// </summary>
    public struct CacheFileEntry
    {
        /// <summary>
        /// Exposes public member <c>Parent</c>.
        /// </summary>
        public uint Parent;
        /// <summary>
        /// Exposes public member <c>Next</c>.
        /// </summary>
        public uint Next;
        /// <summary>
        /// Exposes public member <c>Name</c>.
        /// </summary>
        public CacheFileName Name;
        /// <summary>
        /// Exposes public member <c>Hash</c>.
        /// </summary>
        public Hash256 Hash;
    }

    /// <summary>
    /// Defines the public class <c>GenCacheFile</c>.
    /// </summary>
    public class GenCacheFile
    {
    }
}
