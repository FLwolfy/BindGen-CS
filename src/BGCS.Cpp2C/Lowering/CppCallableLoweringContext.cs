using BGCS.Cpp2C.Configuration;
using BGCS.CppAst.Model.Declarations;

namespace BGCS.Cpp2C.Lowering;

/// <summary>Analysis context borrowed by a callable lowering for the current generation.</summary>
/// <param name="configuration">
/// Configuration owned by the current generation; extensions must not mutate it during analysis.
/// </param>
/// <param name="declaringType">
/// Owning native class, or null for a free function.
/// </param>
/// <param name="defaultExportName">
/// Collision-resolved symbol proposed by bridge analysis.
/// </param>
public sealed record CppCallableLoweringContext(
    Cpp2CGeneratorConfig configuration,
    CppClass? declaringType,
    string defaultExportName
);
