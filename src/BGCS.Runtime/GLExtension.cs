using System;

namespace BGCS.Runtime
{
    /// <summary>
    /// Base type for OpenGL extension loaders that resolve function pointers through a context-specific table.
    /// </summary>
    public abstract class GLExtension : IDisposable
    {
        protected FunctionTable funcTable = null!;
        protected IGLContext context = null!;
        private readonly int length;

        /// <summary>
        /// Initializes a new extension wrapper with a fixed function table size.
        /// </summary>
        /// <param name="length">Number of function pointer slots required by this extension.</param>
        public GLExtension(int length)
        {
            this.length = length;
        }

        /// <summary>
        /// Checks whether this extension is available on the provided context.
        /// </summary>
        /// <param name="context">Context to probe.</param>
        /// <returns><see langword="true"/> when the extension can be loaded; otherwise <see langword="false"/>.</returns>
        public abstract bool IsSupported(IGLContext context);

        /// <summary>
        /// Initializes the extension by creating its function table and resolving all required entries.
        /// </summary>
        /// <param name="context">Current graphics context used to resolve symbols.</param>
        public void Init(IGLContext context)
        {
            this.context = context;
            funcTable = new(context, length);
            InitTable(funcTable);
        }

        /// <summary>
        /// Populates the function table with extension entry points.
        /// </summary>
        /// <param name="funcTable">Function table allocated for this extension.</param>
        protected abstract void InitTable(FunctionTable funcTable);

        /// <summary>
        /// Releases resources allocated by this extension instance.
        /// </summary>
        public void Dispose()
        {
            funcTable.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
