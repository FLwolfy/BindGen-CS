namespace BGCS.Runtime
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>
    /// Interop helper methods for unmanaged memory, delegate pointers and native string encoding.
    /// </summary>
    public static unsafe class Utils
    {
        /// <summary>
        /// Allocates unmanaged memory for a contiguous array of <typeparamref name = "T"/>.
        /// </summary>
        /// <typeparam name = "T">Unmanaged element type.</typeparam>
        /// <param name = "size">Element count to allocate.</param>
        /// <returns>Pointer to allocated memory.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The element count is negative.
        /// </exception>
        /// <exception cref="OverflowException">
        /// The required byte count exceeds the supported allocation size.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T* Alloc<T>(int size)
            where T : unmanaged
        {
            ArgumentOutOfRangeException.ThrowIfNegative(size);
            return size == 0 ? null : (T*)Marshal.AllocHGlobal(checked(size * sizeof(T)));
        }
        /// <summary>
        /// Frees memory previously allocated by <see cref = "Alloc{T}(int)"/>.
        /// </summary>
        /// <param name = "ptr">Pointer to free.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Free(void* ptr) => Marshal.FreeHGlobal((nint)ptr);
        /// <summary>
        /// Frees a COM BSTR allocation.
        /// </summary>
        /// <param name = "ptr">Pointer returned by <see cref = "Marshal.StringToBSTR(string)"/>.</param>
        public static void FreeBSTR(void* ptr) => Marshal.FreeBSTR((nint)ptr);
        /// <summary>
        /// Gets or sets the maximum allowed size for <c>stackalloc</c> during marshalling (default: 2 KiB).
        /// </summary>
        /// <remarks>
        /// <para><strong>Warning:</strong> Setting this value too high may cause a <see cref = "StackOverflowException"/>.</para>
        /// <para>Adjust with caution based on available stack space and application needs.</para>
        /// </remarks>
        public static int maxStackallocSize = 2048;
        /// <summary>
        /// Converts a managed delegate instance to a function pointer.
        /// </summary>
        /// <typeparam name = "T">Delegate type.</typeparam>
        /// <param name = "d">Delegate instance; may be <see langword="null"/>.</param>
        /// <returns>Function pointer address, or <c>0</c> when <paramref name = "d"/> is <see langword="null"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nint GetFunctionPointerForDelegate<T>(T? d)
            where T : Delegate
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
        /// <typeparam name = "T">Delegate type to create.</typeparam>
        /// <param name = "ptr">Function pointer, or <see langword="null"/>.</param>
        /// <returns>Delegate instance, or <see langword="null"/> when <paramref name = "ptr"/> is null.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T? GetDelegateForFunctionPointer<T>(void* ptr)
            where T : Delegate
        {
            if (ptr == null)
            {
                return null;
            }

            return Marshal.GetDelegateForFunctionPointer<T>((nint)ptr);
        }

        /// <summary>
        /// Counts UTF8 encoded bytes without a trailing null terminator.
        /// </summary>
        /// <param name="str">
        /// The non-null managed string to encode.
        /// </param>
        /// <returns>
        /// The required encoded byte count, including zero for an empty string.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The string is null.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteCountUTF8(string str)
        {
            return Encoding.UTF8.GetByteCount(str);
        }

        /// <summary>
        /// Counts UTF16 encoded bytes without a trailing null terminator.
        /// </summary>
        /// <param name="str">
        /// The non-null managed string to encode.
        /// </param>
        /// <returns>
        /// The required encoded byte count, including zero for an empty string.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The string is null.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteCountUTF16(string str)
        {
            return Encoding.Unicode.GetByteCount(str);
        }

        /// <summary>
        /// Encodes a managed string to UTF-8 bytes into an existing unmanaged buffer.
        /// </summary>
        /// <param name = "str">Source string.</param>
        /// <param name = "data">Destination buffer.</param>
        /// <param name = "size">Destination capacity in bytes.</param>
        /// <returns>Number of bytes written.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EncodeStringUTF8(
            string str,
            byte* data,
            int size
        ) {
            fixed (char* pStr = str)
            {
                return Encoding.UTF8.GetBytes(pStr, str.Length, data, size);
            }
        }

        /// <summary>
        /// Encodes a managed string to UTF-16 bytes into an existing unmanaged buffer.
        /// </summary>
        /// <param name = "str">Source string.</param>
        /// <param name = "data">Destination character buffer.</param>
        /// <param name = "size">Destination capacity in bytes.</param>
        /// <returns>Number of bytes written.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EncodeStringUTF16(
            string str,
            char* data,
            int size
        ) {
            fixed (char* pStr = str)
            {
                return Encoding.Unicode.GetBytes(pStr, str.Length, (byte*)data, size);
            }
        }

        /// <summary>
        /// Decodes borrowed null-terminated UTF8 memory without releasing its allocation.
        /// </summary>
        /// <param name="data">
        /// The non-null readable pointer, valid through the terminating null element.
        /// </param>
        /// <returns>
        /// The decoded managed string, including an empty string when the first element is the terminator.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The native pointer is null.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string DecodeStringUTF8(byte* data)
        {
            int length = CStringLength(data);
            return Encoding.UTF8.GetString(data, length);
        }

        /// <summary>
        /// Decodes borrowed null-terminated UTF16 memory without releasing its allocation.
        /// </summary>
        /// <param name="data">
        /// The non-null readable pointer, valid through the terminating null element.
        /// </param>
        /// <returns>
        /// The decoded managed string, including an empty string when the first element is the terminator.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The native pointer is null.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string DecodeStringUTF16(char* data)
        {
            int length = CStringLength(data);
            return new(data, 0, length);
        }

        /// <summary>
        /// Computes the character length of a null-terminated UTF-16 string.
        /// </summary>
        /// <param name = "pointer">Pointer to null-terminated UTF-16 data.</param>
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
        /// <param name = "pointer">Pointer to null-terminated UTF-8 data.</param>
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
        /// Decodes a borrowed COM BSTR using its recorded length without releasing the allocation.
        /// </summary>
        /// <param name="data">
        /// The non-null valid BSTR pointer whose lifetime is retained by the caller.
        /// </param>
        /// <returns>
        /// A managed copy of the BSTR contents; embedded null characters are retained.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The BSTR pointer is null.
        /// </exception>
        public static string DecodeStringBSTR(void* data)
        {
            return Marshal.PtrToStringBSTR((nint)data);
        }

        /// <summary>
        /// Allocates unmanaged UTF-8 memory and copies a managed string including trailing null terminator.
        /// </summary>
        /// <param name = "str">Source string.</param>
        /// <returns>Pointer to allocated UTF-8 data.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte* StringToUTF8Ptr(string str)
        {
            var size = GetByteCountUTF8(str);
            var ptr = Alloc<byte>(checked(size + 1));
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
        /// <param name = "str">Source string.</param>
        /// <returns>Pointer to allocated UTF-16 data.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static char* StringToUTF16Ptr(string str)
        {
            var size = GetByteCountUTF16(str);
            var ptr = Alloc<byte>(checked(size + sizeof(char)));
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
        /// <param name = "str">Source string.</param>
        /// <returns>Pointer to allocated BSTR memory.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* StringToBSTR(string str)
        {
            return (void*)Marshal.StringToBSTR(str);
        }

        /// <summary>
        /// Returns the byte count of the elements in a contiguous unmanaged array.
        /// </summary>
        /// <typeparam name = "T">Unmanaged array element type.</typeparam>
        /// <param name = "array">Array instance.</param>
        /// <returns>The exact element count multiplied by the unmanaged element size.</returns>
        /// <exception cref="ArgumentNullException">
        /// The array is null.
        /// </exception>
        /// <exception cref="OverflowException">
        /// The byte count exceeds a 32-bit signed integer.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteCountArray<T>(T[] array) where T : unmanaged
        {
            ArgumentNullException.ThrowIfNull(array);
            return checked(array.Length * sizeof(T));
        }
    }
}
