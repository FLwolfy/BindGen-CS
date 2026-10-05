using BGCS.Cpp2C.Configuration;
namespace BGCS.Cpp2C.Lowering;

/// <summary>Analysis context borrowed by a type lowering for the current generation.</summary>
/// <param name="configuration">
/// Configuration owned by the current generation; extensions must not mutate it during analysis.
/// </param>
/// <param name="use">
/// Native declaration position being analyzed.
/// </param>
public sealed record CppTypeLoweringContext(
    Cpp2CGeneratorConfig configuration,
    CppTypeLoweringUse use
);
