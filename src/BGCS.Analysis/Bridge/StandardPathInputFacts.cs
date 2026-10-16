using System;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Checks the finite standard filesystem UTF-8 input protocol without assigning it to custom path classes.</summary>
public static class StandardPathInputFacts
{
    /// <summary>The finite counted UTF-8 input cap, shared by native materialization and managed text metadata.</summary>
    public const int C_MAXIMUM_CODE_UNITS = 1048576;
    /// <summary>Requires the actual standard path class and one compiler-established C++20 char8_t identity.</summary>
    /// <param name="source">The detached source-language graph.</param>
    /// <param name="path">The canonical path type or an ordinary alias.</param>
    /// <returns>The exact unqualified native UTF-8 code-unit identity.</returns>
    public static StableId ReadElement(
        NativeModule source,
        StableId path
    ) {
        ArgumentNullException.ThrowIfNull(source);
        NativeType actual = StandardLibraryFactReader.Resolve(source, path);
        if (new StandardLibraryFactReader().Read(source, actual.id)?.family != BGCS.Contracts.Bridge.StandardValueKind.Path
            || actual.declaration is not StableId declaration || !source.GetDeclaration(declaration).isCompleteDefinition)
            throw new NotSupportedException("UTF-8 path input requires the complete actual std::filesystem::path type; custom path construction needs an explicit native shim.");
        NativeType[] units = source.types.Where(type => type.category == NativeTypeCategory.Primitive && type.primitiveKind == "Char8")
            .DistinctBy(type => type.id).ToArray();
        if (units.Length != 1 || units[0].size != 1 || units[0].alignment != 1 || units[0].isSigned != false)
            throw new NotSupportedException("Standard filesystem UTF-8 input requires one compiler-established one-byte unsigned char8_t type and C++20 or later.");
        return units[0].id;
    }
}
