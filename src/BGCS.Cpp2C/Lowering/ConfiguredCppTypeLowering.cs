using BGCS.Core.Extensibility;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;

namespace BGCS.Cpp2C.Lowering;

internal sealed class ConfiguredCppTypeLowering(CppTypeLoweringRecipe recipe) : ICppTypeLowering, ICacheFingerprintProvider
{
    public string name => recipe.name;
    public int priority => recipe.priority;

    public bool CanLower(
        CppType type,
        CppTypeLoweringContext context
    ) => Glob.IsMatch(type.GetDisplayName(), recipe.typePattern) || type is CppClass cppClass && Glob.IsMatch(cppClass.fullName, recipe.typePattern);
    public CppTypeLoweringPlan CreatePlan(
        CppType type,
        CppTypeLoweringContext context
    ) => new(this.name, recipe.kind, recipe.cAbiType, recipe.marshalling, recipe.ownership)
    {
        abiShape = recipe.abiShape,
        abiParameters = recipe.abiParameters.ToArray(),
        parameterToCppExpression = recipe.parameterToCppExpression,
        returnToCExpression = recipe.returnToCExpression,
        requiresCleanup = recipe.requiresCleanup,
        cleanupFunction = recipe.cleanupFunction,
        allocatorKind = recipe.allocatorKind,
        allocatorFunction = recipe.allocatorFunction,
        managedProjection = recipe.managedProjection,
        requiredHeaders = recipe.requiredHeaders.ToArray(),
        safety = recipe.safety
    };
    public string GetCacheFingerprint() => System.Text.Json.JsonSerializer.Serialize(recipe);
}
