using System.Threading;

namespace BGCS.Runtime
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>
    /// Simple pooled unmanaged allocator used by generated marshalling paths.
    /// </summary>
    /// <remarks>
    /// This allocator reuses a bounded set of previously allocated buffers to reduce allocation churn.
    /// Call <see cref="Free"/> for every pointer returned by <see cref="Alloc{T}(int)"/>.
    /// </remarks>
    public static unsafe class MemoryPool
    {
        private static int stack;
        private static readonly Entry* entries;
        const int poolSize = 1024;

        static MemoryPool()
        {
            entries = Utils.Alloc<Entry>(poolSize);
            Unsafe.InitBlockUnaligned(entries, 0, (uint)(sizeof(Entry) * poolSize));
            stack = poolSize;
        }

        /// <summary>
        /// Describes one pooled allocation slot.
        /// </summary>
        public unsafe struct Entry
        {
            /// <summary>
            /// Pointer to pooled memory.
            /// </summary>
            public void* Data;
            /// <summary>
            /// Allocated byte length of <see cref="Data"/>.
            /// </summary>
            public uint Length;
        }

        /// <summary>
        /// Allocates unmanaged memory for <typeparamref name="T"/> elements, reusing pooled buffers when possible.
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <param name="length">Requested number of elements.</param>
        /// <returns>Pointer to unmanaged storage.</returns>
        public static void* Alloc<T>(int length) where T : unmanaged
        {
            int location = Interlocked.Decrement(ref stack);
            if (location > 0)
            {
                Interlocked.Increment(ref stack);
                return Utils.Alloc<T>(length);
            }

            Entry* entry = entries + location;
            uint computedSize = (uint)(sizeof(T) * length);
            if (entry->Data == null)
            {
                entry->Data = Utils.Alloc<T>(length);
                entry->Length = computedSize;
            }
            else if (entry->Length < computedSize)
            {
                Utils.Free(entry->Data);
                entry->Data = Utils.Alloc<T>(length);
                entry->Length = computedSize;
            }

            return entry->Data;
        }

        /// <summary>
        /// Returns memory to the pool, or frees it immediately when it was not pool-managed.
        /// </summary>
        /// <param name="ptr">Pointer previously returned by <see cref="Alloc{T}(int)"/>.</param>
        public static void Free(void* ptr)
        {
            Entry* end = entries + poolSize;
            Entry* current = end - stack;

            uint len = 0;
            while (current != end)
            {
                if (current->Data == ptr)
                {
                    len = current->Length;
                    break;
                }
                current++;
            }

            if (len == 0) // if len 0 then we are outside the stack.
            {
                Utils.Free(ptr);
                return;
            }

            int location = Interlocked.Increment(ref stack);
            Entry* entry = entries + location;
        }
    }

    /// <summary>
    /// Interop helper methods for unmanaged memory, delegate pointers and native string encoding.
    /// </summary>
    public static unsafe class Utils
    {
        /// <summary>
        /// Allocates unmanaged memory for a contiguous array of <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged element type.</typeparam>
        /// <param name="size">Element count to allocate.</param>
        /// <returns>Pointer to allocated memory.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T* Alloc<T>(int size) where T : unmanaged => (T*)Marshal.AllocHGlobal(size * sizeof(T));

        /// <summary>
        /// Frees memory previously allocated by <see cref="Alloc{T}(int)"/>.
        /// </summary>
        /// <param name="ptr">Pointer to free.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Free(void* ptr) => Marshal.FreeHGlobal((nint)ptr);

        /// <summary>
        /// Frees a COM BSTR allocation.
        /// </summary>
        /// <param name="ptr">Pointer returned by <see cref="Marshal.StringToBSTR(string)"/>.</param>
        public static void FreeBSTR(void* ptr) => Marshal.FreeBSTR((nint)ptr);

        /// <summary>
        /// Gets or sets the maximum allowed size for <c>stackalloc</c> during marshalling (default: 2 KiB).
        /// </summary>
        /// <remarks>
        /// <para><strong>Warning:</strong> Setting this value too high may cause a <see cref="StackOverflowException"/>.</para>
        /// <para>Adjust with caution based on available stack space and application needs.</para>
        /// </remarks>
        public static int MaxStackallocSize = 2048;

        /// <summary>
        /// Converts a managed delegate instance to a function pointer.
        /// </summary>
        /// <typeparam name="T">Delegate type.</typeparam>
        /// <param name="d">Delegate instance; may be <see langword="null"/>.</param>
        /// <returns>Function pointer address, or <c>0</c> when <paramref name="d"/> is <see langword="null"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nint GetFunctionPointerForDelegate<T>(T? d) where T : Delegate
        {
            if (d == null)
            {
                return 0;
            }
            return Marshal.GetFunctionPointerForDelegate(d);
        }

        /// <summary>
        /// Converts an unmanaged function pointer to a managed delegate instance.
        /// </summary>
        /// <typeparam name="T">Delegate type to create.</typeparam>
        /// <param name="ptr">Function pointer, or <see langword="null"/>.</param>
        /// <returns>Delegate instance, or <see langword="null"/> when <paramref name="ptr"/> is null.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T? GetDelegateForFunctionPointer<T>(void* ptr) where T : Delegate
        {
            if (ptr == null)
            {
                return null;
            }
            return Marshal.GetDelegateForFunctionPointer<T>((nint)ptr);
        }

        /// <summary>
        /// Gets UTF-8 byte count (without null terminator) for a managed string.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteCountUTF8(string str)
        {
            return Encoding.UTF8.GetByteCount(str);
        }

        /// <summary>
        /// Gets UTF-16 byte count (without null terminator) for a managed string.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteCountUTF16(string str)
        {
            return Encoding.Unicode.GetByteCount(str);
        }

        /// <summary>
        /// Encodes a managed string to UTF-8 bytes into an existing unmanaged buffer.
        /// </summary>
        /// <param name="str">Source string.</param>
        /// <param name="data">Destination buffer.</param>
        /// <param name="size">Destination capacity in bytes.</param>
        /// <returns>Number of bytes written.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EncodeStringUTF8(string str, byte* data, int size)
        {
            fixed (char* pStr = str)
            {
                return Encoding.UTF8.GetBytes(pStr, str.Length, data, size);
            }
        }

        /// <summary>
        /// Encodes a managed string to UTF-16 bytes into an existing unmanaged buffer.
        /// </summary>
        /// <param name="str">Source string.</param>
        /// <param name="data">Destination character buffer.</param>
        /// <param name="size">Destination capacity in bytes.</param>
        /// <returns>Number of bytes written.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EncodeStringUTF16(string str, char* data, int size)
        {
            fixed (char* pStr = str)
            {
                return Encoding.Unicode.GetBytes(pStr, str.Length, (byte*)data, size);
            }
        }

        /// <summary>
        /// Decodes a null-terminated UTF-8 string from unmanaged memory.
        /// </summary>
        /// <param name="data">Pointer to UTF-8 null-terminated bytes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string DecodeStringUTF8(byte* data)
        {
            int length = CStringLength(data);
            return Encoding.UTF8.GetString(data, length);
        }

        /// <summary>
        /// Decodes a null-terminated UTF-16 string from unmanaged memory.
        /// </summary>
        /// <param name="data">Pointer to UTF-16 null-terminated characters.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string DecodeStringUTF16(char* data)
        {
            int length = CStringLength(data);
            return new(data, 0, length);
        }

        /// <summary>
        /// Computes the character length of a null-terminated UTF-16 string.
        /// </summary>
        /// <param name="pointer">Pointer to null-terminated UTF-16 data.</param>
        /// <returns>Number of characters before the terminator.</returns>
        public static int CStringLength(char* pointer)
        {
            if (pointer == null)
            {
                throw new ArgumentNullException(nameof(pointer));
            }

            // Find the length of the null-terminated string
            int length = 0;
            while (pointer[length] != '\0')
            {
                length++;
            }

            return length;
        }

        /// <summary>
        /// Computes the byte length of a null-terminated UTF-8 string.
        /// </summary>
        /// <param name="pointer">Pointer to null-terminated UTF-8 data.</param>
        /// <returns>Number of bytes before the terminator.</returns>
        public static int CStringLength(byte* pointer)
        {
            if (pointer == null)
            {
                throw new ArgumentNullException(nameof(pointer));
            }

            // Find the length of the null-terminated string
            int length = 0;
            while (pointer[length] != '\0')
            {
                length++;
            }

            return length;
        }

        /// <summary>
        /// Decodes a COM BSTR string from unmanaged memory.
        /// </summary>
        /// <param name="data">Pointer to BSTR memory.</param>
        public static string DecodeStringBSTR(void* data)
        {
            return Marshal.PtrToStringBSTR((nint)data);
        }

        /// <summary>
        /// Allocates unmanaged UTF-8 memory and copies a managed string including trailing null terminator.
        /// </summary>
        /// <param name="str">Source string.</param>
        /// <returns>Pointer to allocated UTF-8 data.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte* StringToUTF8Ptr(string str)
        {
            var size = GetByteCountUTF8(str);
            var ptr = Alloc<byte>(size + 1);
            fixed (char* pStr = str)
            {
                Encoding.UTF8.GetBytes(pStr, str.Length, ptr, size);
            }
            ptr[size] = 0;
            return ptr;
        }

        /// <summary>
        /// Allocates unmanaged UTF-16 memory and copies a managed string including trailing null terminator.
        /// </summary>
        /// <param name="str">Source string.</param>
        /// <returns>Pointer to allocated UTF-16 data.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static char* StringToUTF16Ptr(string str)
        {
            var size = GetByteCountUTF16(str);
            var ptr = Alloc<byte>(size);
            fixed (char* pStr = str)
            {
                Encoding.Unicode.GetBytes(pStr, str.Length, ptr, size);
            }
            var result = (char*)ptr;
            result[str.Length] = '\0';
            return result;
        }

        /// <summary>
        /// Allocates a COM BSTR from a managed string.
        /// </summary>
        /// <param name="str">Source string.</param>
        /// <returns>Pointer to allocated BSTR memory.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* StringToBSTR(string str)
        {
            return (void*)Marshal.StringToBSTR(str);
        }

        /// <summary>
        /// Returns the byte count occupied by an unmanaged array reference, using native pointer-sized stride.
        /// </summary>
        /// <typeparam name="T">Array element type.</typeparam>
        /// <param name="array">Array instance.</param>
        /// <returns>Estimated byte size.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteCountArray<T>(T[] array) => array.Length * sizeof(nuint);
    }
}
