using BGCS.Core.Extensibility;
using BGCS.CppAst.Model.Declarations;

namespace BGCS.Cpp2C.Lowering;

internal sealed class ConfiguredCppCallableLowering(CppCallableLoweringRecipe recipe) : ICppCallableLowering, ICacheFingerprintProvider
{
    public string name => recipe.name;
    public int priority => recipe.priority;

    public bool CanLower(
        CppFunction function,
        CppCallableLoweringContext context
    ) => Glob.IsMatch(string.IsNullOrEmpty(function.fullParentName) ? function.name : function.fullParentName + "::" + function.name, recipe.functionPattern) || Glob.IsMatch(function.name, recipe.functionPattern);
    public CppCallableLoweringPlan CreatePlan(
        CppFunction function,
        CppCallableLoweringContext context
    ) => new(this.name, string.IsNullOrWhiteSpace(recipe.exportName) ? context.defaultExportName : recipe.exportName)
    {
        exclude = recipe.exclude,
        invocationExpression = recipe.invocationExpression,
        requiredHeaders = recipe.requiredHeaders.ToArray(),
        safety = recipe.safety
    };
    public string GetCacheFingerprint() => System.Text.Json.JsonSerializer.Serialize(recipe);
}
