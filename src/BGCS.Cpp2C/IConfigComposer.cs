namespace BGCS.Cpp2C
{
    /// <summary>
    /// Defines the public interface <c>IConfigComposer</c>.
    /// </summary>
    public interface IConfigComposer
    {
        void Compose(ref Cpp2CGeneratorConfig config);

        /// <summary>
        /// Composes a configuration using an explicit directory for relative base-configuration references.
        /// </summary>
        /// <remarks>
        /// The default implementation preserves compatibility with existing custom composers. Implementations that
        /// read relative files should override this overload instead of relying on the process current directory.
        /// </remarks>
        void Compose(ref Cpp2CGeneratorConfig config, string baseDirectory)
        {
            Compose(ref config);
        }
    }
}
