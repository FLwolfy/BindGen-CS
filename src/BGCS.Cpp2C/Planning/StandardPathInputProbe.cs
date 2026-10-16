using System;
using System.Linq;
using BGCS.Analysis.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Cpp2C.Planning;

/// <summary>Requests the compiler's actual UTF-8 unit fact without exposing a native callable or guessing a scalar ABI.</summary>
public static class StandardPathInputProbe
{
    /// <summary>The exact private alias whose ordinary declaration is never a public binding root.</summary>
    public const string C_ALIAS = "BGCSGeneratedPathInputFacts::Utf8Unit";
    /// <summary>Gets the bounded compiler probe; pre-C++20 configurations simply do not declare the optional fact.</summary>
    public static string postamble => "\n#if defined(__cpp_char8_t)\nnamespace BGCSGeneratedPathInputFacts { using Utf8Unit = char8_t; }\n#endif\n";

    /// <summary>Finds the exact compiler-established optional alias before neutral source selection.</summary>
    /// <param name="module">The original compiler-produced source graph.</param>
    /// <returns>The metadata-only declaration, or null when the selected native dialect has no char8_t support.</returns>
    public static StableId? Find(NativeModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        NativeDeclaration[] aliases = module.declarations.Where(declaration => declaration.kind == "Typedef" && declaration.qualifiedName == C_ALIAS).ToArray();
        if (aliases.Length == 0) return null;
        if (aliases.Length != 1 || aliases[0].type is not StableId type)
            throw new InvalidOperationException("The private standard path input probe did not preserve one compiler-established alias.");
        NativeType unit = StandardLibraryFactReader.Resolve(module, type);
        if (unit.category != NativeTypeCategory.Primitive || unit.primitiveKind != "Char8" || unit.size != 1 || unit.alignment != 1 || unit.isSigned != false)
            throw new NotSupportedException("The compiler's standard path UTF-8 unit is not a one-byte unsigned char8_t type.");
        return aliases[0].id;
    }
}
