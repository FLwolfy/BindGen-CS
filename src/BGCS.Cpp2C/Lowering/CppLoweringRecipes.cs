using System.Text.RegularExpressions;
using BGCS.Core.Extensibility;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.Intermediate;

namespace BGCS.Cpp2C.Lowering;

/// <summary>Declarative type-lowering recipe serialized in bridge configuration.</summary>
public sealed class CppTypeLoweringRecipe
{
    public string Name { get; set; } = string.Empty;
    public string TypePattern { get; set; } = string.Empty;
    public int Priority { get; set; } = 100;
    public CppTypeLoweringKind Kind { get; set; } = CppTypeLoweringKind.Custom;
    public string CAbiType { get; set; } = "void*";
    public MarshallingStrategy Marshalling { get; set; } = MarshallingStrategy.Handle;
    public BindingOwnership Ownership { get; set; } = BindingOwnership.Borrowed;
    public CppAbiShape AbiShape { get; set; } = CppAbiShape.Direct;
    public List<CppAbiParameter> AbiParameters { get; set; } = [];
    public string? ParameterToCppExpression { get; set; }
    public string? ReturnToCExpression { get; set; }
    public bool RequiresCleanup { get; set; }
    public string? CleanupFunction { get; set; }
    public BindingAllocatorKind AllocatorKind { get; set; } = BindingAllocatorKind.Unspecified;
    public string? AllocatorFunction { get; set; }
    public CppManagedProjection? ManagedProjection { get; set; }
    public List<string> RequiredHeaders { get; set; } = [];
    public CppLoweringSafety Safety { get; set; } = CppLoweringSafety.UserAsserted;
}

/// <summary>Declarative callable-lowering recipe serialized in bridge configuration.</summary>
public sealed class CppCallableLoweringRecipe
{
    public string Name { get; set; } = string.Empty;
    public string FunctionPattern { get; set; } = string.Empty;
    public int Priority { get; set; } = 100;
    public string? ExportName { get; set; }
    public bool Exclude { get; set; }
    public string? InvocationExpression { get; set; }
    public List<string> RequiredHeaders { get; set; } = [];
    public CppLoweringSafety Safety { get; set; } = CppLoweringSafety.UserAsserted;
}

/// <summary>Explicit, user-owned C ABI shim copied into bridge output and its build manifest.</summary>
public sealed class CppNativeShim
{
    public string Name { get; set; } = string.Empty;
    public List<string> PublicHeaders { get; set; } = [];
    public List<string> SourceFiles { get; set; } = [];
    public CppLoweringSafety Safety { get; set; } = CppLoweringSafety.UserAsserted;
}

internal sealed class ConfiguredCppTypeLowering(CppTypeLoweringRecipe recipe) : ICppTypeLowering, ICacheFingerprintProvider
{
    public string Name => recipe.Name;
    public int Priority => recipe.Priority;

    public bool CanLower(CppType type, CppTypeLoweringContext context) =>
        Glob.IsMatch(type.GetDisplayName(), recipe.TypePattern) ||
        type is CppClass cppClass && Glob.IsMatch(cppClass.FullName, recipe.TypePattern);

    public CppTypeLoweringPlan CreatePlan(CppType type, CppTypeLoweringContext context) =>
        new(Name, recipe.Kind, recipe.CAbiType, recipe.Marshalling, recipe.Ownership)
        {
            AbiShape = recipe.AbiShape,
            AbiParameters = recipe.AbiParameters.ToArray(),
            ParameterToCppExpression = recipe.ParameterToCppExpression,
            ReturnToCExpression = recipe.ReturnToCExpression,
            RequiresCleanup = recipe.RequiresCleanup,
            CleanupFunction = recipe.CleanupFunction,
            AllocatorKind = recipe.AllocatorKind,
            AllocatorFunction = recipe.AllocatorFunction,
            ManagedProjection = recipe.ManagedProjection,
            RequiredHeaders = recipe.RequiredHeaders.ToArray(),
            Safety = recipe.Safety
        };

    public string GetCacheFingerprint() => System.Text.Json.JsonSerializer.Serialize(recipe);
}

internal sealed class ConfiguredCppCallableLowering(CppCallableLoweringRecipe recipe) : ICppCallableLowering, ICacheFingerprintProvider
{
    public string Name => recipe.Name;
    public int Priority => recipe.Priority;

    public bool CanLower(CppFunction function, CppCallableLoweringContext context) =>
        Glob.IsMatch(string.IsNullOrEmpty(function.FullParentName)
            ? function.Name
            : function.FullParentName + "::" + function.Name, recipe.FunctionPattern) ||
        Glob.IsMatch(function.Name, recipe.FunctionPattern);

    public CppCallableLoweringPlan CreatePlan(CppFunction function, CppCallableLoweringContext context) =>
        new(Name, string.IsNullOrWhiteSpace(recipe.ExportName) ? context.DefaultExportName : recipe.ExportName)
        {
            Exclude = recipe.Exclude,
            InvocationExpression = recipe.InvocationExpression,
            RequiredHeaders = recipe.RequiredHeaders.ToArray(),
            Safety = recipe.Safety
        };

    public string GetCacheFingerprint() => System.Text.Json.JsonSerializer.Serialize(recipe);
}

internal static partial class Glob
{
    internal static bool IsMatch(string value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;
        string expression = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value.Replace(" ", string.Empty, StringComparison.Ordinal),
            expression.Replace("\\ ", string.Empty, StringComparison.Ordinal), RegexOptions.CultureInvariant);
    }
}
