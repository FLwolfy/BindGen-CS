namespace BGCS.Runtime
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Owns a contiguous table of native function pointers used by generated bindings.
    /// </summary>
    /// <remarks>
    /// The table can either wrap an externally managed pointer array or allocate and manage its own storage.
    /// </remarks>
    public unsafe class FunctionTable : IDisposable
    {
        private void** _vtable;
        private int length;
        private readonly INativeContext context;

        /// <summary>
        /// Wraps an existing function table pointer without taking ownership of its storage.
        /// </summary>
        /// <param name="vtable">Pointer to function pointer array.</param>
        /// <param name="length">Number of entries in <paramref name="vtable"/>.</param>
        public FunctionTable(void** vtable, int length)
        {
            context = null!;
            _vtable = vtable;
            this.length = length;
        }

        /// <summary>
        /// Creates a managed function table and resolves entries from a native library handle.
        /// </summary>
        /// <param name="library">Native module handle.</param>
        /// <param name="length">Initial number of slots to allocate.</param>
        public FunctionTable(nint library, int length) : this(new NativeLibraryContext(library), length)
        {
        }

        /// <summary>
        /// Creates a managed function table and loads a native library from disk.
        /// </summary>
        /// <param name="libraryPath">Path or logical name of the native library to load.</param>
        /// <param name="length">Initial number of slots to allocate.</param>
        public FunctionTable(string libraryPath, int length) : this(new NativeLibraryContext(libraryPath), length)
        {
        }

        /// <summary>
        /// Creates a managed function table backed by an <see cref="INativeContext"/>.
        /// </summary>
        /// <param name="context">Context used to resolve exported symbols.</param>
        /// <param name="length">Initial number of slots to allocate.</param>
        public FunctionTable(INativeContext context, int length)
        {
            this.context = context;
            _vtable = (void**)Marshal.AllocHGlobal(length * sizeof(void*));
            new Span<nint>(_vtable, length).Clear(); // Fill with null pointers
            this.length = length;
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
        public void Load(int index, string export)
        {
            if (!context.TryGetProcAddress(export, out var address))
            {
                _vtable[index] = null;
                return;
            }

            _vtable[index] = (void*)address;
        }

        /// <summary>
        /// Resizes the table storage.
        /// </summary>
        /// <param name="newLength">Requested slot count.</param>
        public void Resize(int newLength)
        {
            if (newLength == length)
                return;

            _vtable = (void**)Marshal.ReAllocHGlobal((nint)_vtable, (nint)(newLength * sizeof(void*)));
            length = newLength;
        }

        /// <summary>
        /// Gets or sets a function pointer at the specified slot.
        /// </summary>
        /// <param name="index">Slot index.</param>
        public void* this[int index]
        {
            get => _vtable[index];
            set => _vtable[index] = value;
        }

        /// <summary>
        /// Releases table memory and disposes the underlying native context.
        /// </summary>
        public void Free()
        {
            if (_vtable != null)
            {
                Marshal.FreeHGlobal((nint)_vtable);
                _vtable = null;
            }

            context.Dispose();
        }

        /// <summary>
        /// Releases unmanaged resources held by this instance.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Free();
        }
    }
}
