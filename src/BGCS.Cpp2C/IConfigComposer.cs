namespace BGCS.Cpp2C
{
    /// <summary>
    /// Defines the public interface <c>IConfigComposer</c>.
    /// </summary>
    public interface IConfigComposer
    {
        void Compose(ref Cpp2CGeneratorConfig config);
    }
}
