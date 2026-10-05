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
        private void** m_vtable;
        private int m_length;
        private readonly INativeContext m_context;
        private readonly bool m_ownsStorage;
        private readonly bool m_ownsContext;
        private bool m_disposed;
        /// <summary>
        /// Wraps an existing function table pointer without taking ownership of its storage.
        /// </summary>
        /// <param name = "vtable">Pointer to function pointer array.</param>
        /// <param name = "length">Number of entries in <paramref name = "vtable"/>.</param>
        /// <exception cref = "ArgumentOutOfRangeException">The length is negative.</exception>
        /// <exception cref = "ArgumentException">A nonempty table has a null address.</exception>
        public FunctionTable(
            void** vtable,
            int length
        ) {
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            if (vtable == null && length != 0)
                throw new ArgumentException("A nonempty table requires a pointer array.", nameof(vtable));
            this.m_context = null!;
            this.m_vtable = vtable;
            this.m_length = length;
        }

        /// <summary>
        /// Creates a managed function table and resolves entries from a native library handle.
        /// </summary>
        /// <param name = "library">Native module handle.</param>
        /// <param name = "length">Initial number of slots to allocate.</param>
        public FunctionTable(
            nint library,
            int length
        ) : this(() => new NativeLibraryContext(library), length)
        {
        }

        /// <summary>
        /// Creates a managed function table and loads a native library from disk.
        /// </summary>
        /// <param name = "libraryPath">Path or logical name of the native library to load.</param>
        /// <param name = "length">Initial number of slots to allocate.</param>
        public FunctionTable(
            string libraryPath,
            int length
        ) : this(() => new NativeLibraryContext(libraryPath), length)
        {
        }

        /// <summary>
        /// Creates a managed function table backed by an <see cref = "INativeContext"/>.
        /// </summary>
        /// <param name = "context">Context used to resolve exported symbols.</param>
        /// <param name = "length">Initial number of slots to allocate.</param>
        /// <param name = "ownsContext">Whether successful construction transfers context ownership to the table.</param>
        /// <remarks>
        /// Context ownership transfers only after successful construction when ownsContext is true.
        /// </remarks>
        /// <exception cref = "ArgumentNullException">The context is null.</exception>
        /// <exception cref = "ArgumentOutOfRangeException">The length is negative.</exception>
        public FunctionTable(
            INativeContext context,
            int length,
            bool ownsContext = true
        ) {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            this.m_vtable = (void**)Marshal.AllocHGlobal(checked(length * sizeof(void*)));
            new Span<nint>(this.m_vtable, length).Clear(); // Fill with null pointers
            this.m_context = context;
            this.m_length = length;
            m_ownsStorage = true;
            m_ownsContext = ownsContext;
        }

        /// <summary>
        /// Gets the current number of slots in this function table.
        /// </summary>
        public int length => this.m_length;

        /// <summary>
        /// Loads a named export into a table slot.
        /// </summary>
        /// <param name = "index">Target slot index.</param>
        /// <param name = "export">Native export name.</param>
        /// <remarks>
        /// When the export cannot be resolved, the slot is set to <see langword="null"/>.
        /// </remarks>
        /// <exception cref = "ObjectDisposedException">The table has been freed.</exception>
        /// <exception cref = "ArgumentOutOfRangeException">The slot is outside the table.</exception>
        /// <exception cref = "InvalidOperationException">The table borrows storage and has no symbol context.</exception>
        public void Load(
            int index,
            string export
        ) {
            ValidateIndex(index);
            if (!m_ownsStorage)
                throw new InvalidOperationException("A borrowed table has no symbol context.");
            if (!this.m_context.TryGetProcAddress(export, out var address))
            {
                this.m_vtable[index] = null;
                return;
            }

            this.m_vtable[index] = (void*)address;
        }

        /// <summary>
        /// Resolves a required entry and rejects a missing symbol before callers can invoke it.
        /// </summary>
        /// <param name = "index">
        /// The destination slot.
        /// </param>
        /// <param name = "export">
        /// The required native export name.
        /// </param>
        /// <exception cref = "ObjectDisposedException">
        /// The table has been released.
        /// </exception>
        /// <exception cref = "ArgumentOutOfRangeException">
        /// The slot is outside this table.
        /// </exception>
        /// <exception cref = "InvalidOperationException">
        /// The table borrows storage and has no symbol context.
        /// </exception>
        /// <exception cref = "EntryPointNotFoundException">
        /// The context cannot resolve the required export.
        /// </exception>
        public void LoadRequired(
            int index,
            string export
        ) {
            Load(index, export);
            if (this.m_vtable[index] == null)
                throw new EntryPointNotFoundException($"Required native export '{export}' is unavailable.");
        }

        /// <summary>
        /// Resizes owned storage, preserving existing entries and clearing newly allocated slots.
        /// </summary>
        /// <param name = "newLength">Requested slot count.</param>
        /// <exception cref = "ObjectDisposedException">The table has been freed.</exception>
        /// <exception cref = "ArgumentOutOfRangeException">The requested length is negative.</exception>
        /// <exception cref = "InvalidOperationException">The table borrows the caller's storage.</exception>
        public void Resize(int newLength)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(newLength);
            if (!m_ownsStorage)
                throw new InvalidOperationException("A borrowed pointer array cannot be resized.");
            if (newLength == this.m_length)
                return;
            this.m_vtable = (void**)Marshal.ReAllocHGlobal((nint)this.m_vtable, checked((nint)newLength * sizeof(void*)));
            if (newLength > this.m_length)
                new Span<nint>(this.m_vtable + this.m_length, newLength - this.m_length).Clear();
            this.m_length = newLength;
        }

        /// <summary>
        /// Gets or sets a function pointer at the specified slot.
        /// </summary>
        /// <param name = "index">Slot index.</param>
        /// <returns>The stored function address, or null for an unresolved entry.</returns>
        /// <exception cref = "ObjectDisposedException">The table has been freed.</exception>
        /// <exception cref = "ArgumentOutOfRangeException">The slot is outside the table.</exception>
        public void* this[int index]
        {
            get
            {
                ValidateIndex(index);
                return this.m_vtable[index];
            }

            set
            {
                ValidateIndex(index);
                this.m_vtable[index] = value;
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
                Marshal.FreeHGlobal((nint)this.m_vtable);
            this.m_vtable = null;
            this.m_length = 0;
            if (m_ownsContext)
                this.m_context.Dispose();
        }

        /// <summary>
        /// Releases unmanaged resources held by this instance.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Free();
        }

        private FunctionTable(
            Func<INativeContext> createContext,
            int length
        ) {
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            this.m_vtable = (void**)Marshal.AllocHGlobal(checked(length * sizeof(void*)));
            try
            {
                this.m_context = createContext();
            }
            catch
            {
                Marshal.FreeHGlobal((nint)this.m_vtable);
                this.m_vtable = null;
                throw;
            }

            new Span<nint>(this.m_vtable, length).Clear();
            this.m_length = length;
            m_ownsStorage = true;
            m_ownsContext = true;
        }

        private void ValidateIndex(int index)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if ((uint)index >= (uint)this.m_length)
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
