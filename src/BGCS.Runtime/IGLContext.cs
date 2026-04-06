namespace BGCS.Runtime
{
    /// <summary>
    /// Specialization of <see cref="INativeContext"/> for OpenGL-like APIs that require a current context.
    /// </summary>
    public interface IGLContext : INativeContext
    {
        /// <summary>
        /// Gets the native handle of the graphics context.
        /// </summary>
        nint Handle { get; }

        /// <summary>
        /// Gets whether this context is currently bound to the calling thread.
        /// </summary>
        bool IsCurrent { get; }

        /// <summary>
        /// Makes this graphics context current on the calling thread.
        /// </summary>
        void MakeCurrent();

        /// <summary>
        /// Swaps the front/back buffers associated with this context.
        /// </summary>
        void SwapBuffers();

        /// <summary>
        /// Sets swap interval (for example VSync behavior) for this context.
        /// </summary>
        /// <param name="interval">Implementation-defined interval value.</param>
        void SwapInterval(int interval);
    }
}
