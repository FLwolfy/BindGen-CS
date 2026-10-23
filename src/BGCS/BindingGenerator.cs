namespace BGCS;

/// <summary>Composes the complete source generator with its replaceable standard implementations.</summary>
public static class BindingGenerator
{
    /// <summary>Creates a generator with isolated Clang parsing, observed host paths and desktop .NET call rules.</summary>
    /// <returns>The caller-owned generation host.</returns>
    /// <remarks>No consumer compilation, linking, deployment or project modification is performed.</remarks>
    public static BindingHost CreateDefault() => CreateBuilder().Build();

    /// <summary>Creates the standard composition before explicit extension additions or replacements.</summary>
    /// <returns>A mutable builder that freezes when <see cref="BindingHostBuilder.Build"/> is called.</returns>
    public static BindingHostBuilder CreateBuilder() => Composition.StandardBindingComposition.CreateBuilder();
}
