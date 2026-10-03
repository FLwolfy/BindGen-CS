namespace BGCS.Runtime
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Provides owned or borrowed native function pointer storage for generated bindings.
    /// </summary>
    /// <remarks>
    /// The table can either wrap an externally managed pointer array or allocate and manage its own storage.
    /// </remarks>
    public unsafe class FunctionTable : IDisposable
    {
        private void** _vtable;
        private int length;
        private readonly INativeContext context;
        private readonly bool m_ownsStorage;
        private bool m_disposed;

        /// <summary>
        /// Wraps an existing function table pointer without taking ownership of its storage.
        /// </summary>
        /// <param name="vtable">Pointer to function pointer array.</param>
        /// <param name="length">Number of entries in <paramref name="vtable"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
        /// <exception cref="ArgumentException">A nonempty table has a null address.</exception>
        public FunctionTable(
            void** vtable,
            int length
        ) {
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            if (vtable == null && length != 0)
                throw new ArgumentException("A nonempty table requires a pointer array.", nameof(vtable));
            context = null!;
            _vtable = vtable;
            this.length = length;
        }

        /// <summary>
        /// Creates a managed function table and resolves entries from a native library handle.
        /// </summary>
        /// <param name="library">Native module handle.</param>
        /// <param name="length">Initial number of slots to allocate.</param>
        public FunctionTable(
            nint library,
            int length
        ) : this(new NativeLibraryContext(library), length) { }

        /// <summary>
        /// Creates a managed function table and loads a native library from disk.
        /// </summary>
        /// <param name="libraryPath">Path or logical name of the native library to load.</param>
        /// <param name="length">Initial number of slots to allocate.</param>
        public FunctionTable(
            string libraryPath,
            int length
        ) : this(new NativeLibraryContext(libraryPath), length) { }

        /// <summary>
        /// Creates a managed function table backed by an <see cref="INativeContext"/>.
        /// </summary>
        /// <param name="context">Context used to resolve exported symbols.</param>
        /// <param name="length">Initial number of slots to allocate.</param>
        /// <remarks>
        /// Successful construction transfers context ownership to the table until it is freed.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The context is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
        public FunctionTable(
            INativeContext context,
            int length
        ) {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            _vtable = (void**)Marshal.AllocHGlobal(checked(length * sizeof(void*)));
            new Span<nint>(_vtable, length).Clear(); // Fill with null pointers
            this.context = context;
            this.length = length;
            m_ownsStorage = true;
        }

        /// <summary>
        /// Gets the current number of slots in this function table.
        /// </summary>
        public int Length => length;

        /// <summary>
        /// Loads a named export into a table slot.
        /// </summary>
        /// <param name="index">Target slot index.</param>
        /// <param name="export">Native export name.</param>
        /// <remarks>
        /// When the export cannot be resolved, the slot is set to <see langword="null"/>.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">The table has been freed.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The slot is outside the table.</exception>
        /// <exception cref="InvalidOperationException">The table borrows storage and has no symbol context.</exception>
        public void Load(
            int index,
            string export
        ) {
            ValidateIndex(index);
            if (!m_ownsStorage)
                throw new InvalidOperationException("A borrowed table has no symbol context.");
            if (!context.TryGetProcAddress(export, out var address))
            {
                _vtable[index] = null;
                return;
            }

            _vtable[index] = (void*)address;
        }

        /// <summary>
        /// Resizes owned storage, preserving existing entries and clearing newly allocated slots.
        /// </summary>
        /// <param name="newLength">Requested slot count.</param>
        /// <exception cref="ObjectDisposedException">The table has been freed.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The requested length is negative.</exception>
        /// <exception cref="InvalidOperationException">The table borrows the caller's storage.</exception>
        public void Resize(int newLength)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(newLength);
            if (!m_ownsStorage)
                throw new InvalidOperationException("A borrowed pointer array cannot be resized.");
            if (newLength == length)
                return;

            _vtable = (void**)Marshal.ReAllocHGlobal((nint)_vtable, checked((nint)newLength * sizeof(void*)));
            if (newLength > length)
                new Span<nint>(_vtable + length, newLength - length).Clear();
            length = newLength;
        }

        /// <summary>
        /// Gets or sets a function pointer at the specified slot.
        /// </summary>
        /// <param name="index">Slot index.</param>
        /// <returns>The stored function address, or null for an unresolved entry.</returns>
        /// <exception cref="ObjectDisposedException">The table has been freed.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The slot is outside the table.</exception>
        public void* this[int index]
        {
            get
            {
                ValidateIndex(index);
                return _vtable[index];
            }
            set
            {
                ValidateIndex(index);
                _vtable[index] = value;
            }
        }

        /// <summary>
        /// Releases owned storage and its context once, or ends a borrow without freeing the external array.
        /// </summary>
        public void Free()
        {
            if (m_disposed)
                return;
            m_disposed = true;
            if (m_ownsStorage)
                Marshal.FreeHGlobal((nint)_vtable);
            _vtable = null;
            length = 0;
            context?.Dispose();
        }

        /// <summary>
        /// Releases unmanaged resources held by this instance.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Free();
        }

        private void ValidateIndex(int index)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if ((uint)index >= (uint)length)
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
