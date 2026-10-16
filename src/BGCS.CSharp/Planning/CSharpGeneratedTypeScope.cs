using BGCS.Contracts.Managed;

namespace BGCS.CSharp.Planning;

/// <summary>Resolves the declared scope of generated carriers without changing native identities or local declaration names.</summary>
internal static class CSharpGeneratedTypeScope
{
    internal static string DeclarationNamespace(ManagedApiModel api)
        => CSharpNames.Namespace(api.@namespace) + (api.nestGeneratedTypesInApi ? "." + CSharpNames.Identifier(api.name) : string.Empty);

    internal static string DeclarationReference(
        ManagedApiModel api,
        string name
    )
        => api.nestGeneratedTypesInApi ? "global::" + DeclarationNamespace(api) + "." + CSharpNames.Identifier(name) : CSharpNames.Identifier(name);

    internal static string QualifiedReference(
        ManagedApiModel api,
        string name
    )
        => "global::" + DeclarationNamespace(api) + "." + CSharpNames.Identifier(name);

    internal static string WrapDeclarations(
        ManagedApiModel api,
        string declarations
    )
        => !api.nestGeneratedTypesInApi ? declarations : "public " + (api.style == ManagedApiStyle.Static ? "static" : "sealed")
            + " unsafe partial class " + CSharpNames.Identifier(api.name) + "\n{\n" + declarations + "\n}\n";
}
