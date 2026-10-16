using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class LibraryNameEmitter
{
    internal const string C_NAME = "NativeLibraryName";
    internal static string Member(string libraryName) => "public const string " + C_NAME + " = " + ContextEmitter.Literal(libraryName) + ";";
    internal static string Emit(
        CSharpEmissionPlan plan,
        string libraryName
    ) => plan.api.emitLibraryNameConstant ? "    " + Member(libraryName) + "\n" : string.Empty;
}
