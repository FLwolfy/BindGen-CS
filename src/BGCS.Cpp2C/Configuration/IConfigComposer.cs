namespace BGCS.Cpp2C.Configuration
{
    /// <summary>
    /// Composes configured base documents using an explicit path origin without changing the process working directory.
    /// </summary>
    public interface IConfigComposer
    {
        /// <summary>
        /// Resolves base configurations into the supplied configuration using an explicit path origin.
        /// </summary>
        /// <param name="config">Configuration replaced with the composed result.</param>
        /// <param name="baseDirectory">Directory used to resolve the initial relative base configuration reference.</param>
        void Compose(
            ref Cpp2CGeneratorConfig config,
            string baseDirectory
        );
    }
}
