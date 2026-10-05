using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BGCS.Cpp2C.Configuration;
using BGCS.Intermediate;
using BGCS.Intermediate.Bridges;
using BGCS.Intermediate.Emission;

namespace BGCS.Cpp2C.Lowering;

/// <summary>
/// Freezes lowering source contributions and emits managed conversions from analyzed C ABI carriers.
/// </summary>
public static class CppExtensionArtifactEmitter
{
    /// <summary>
    /// Freezes native extension artifacts before the bridge emitter writes any files.
    /// </summary>
    /// <param name = "config">
    /// The configured lowering contributors and resolved native target.
    /// </param>
    /// <returns>
    /// The source artifacts, including a separate binding umbrella when extensions expose headers.
    /// </returns>
    public static IReadOnlyList<CppBridgeArtifact> CollectNative(Cpp2CGeneratorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        List<CppBridgeArtifact> artifacts = [];
        foreach (CppGeneratedArtifactKind stage in new[]
        {
            CppGeneratedArtifactKind.PublicHeader,
            CppGeneratedArtifactKind.NativeSource,
            CppGeneratedArtifactKind.Resource
        }

        )
        {
            CppArtifactContext context = new(config, config.resolvedTarget.targetId.value, stage);
            foreach (CppGeneratedArtifact artifact in config.lowerings.CollectArtifacts(context))
            {
                if (artifact.kind != stage)
                    throw new InvalidOperationException($"Artifact '{artifact.relativePath}' was returned for stage {stage} but declares {artifact.kind}.");
                string directory = stage switch
                {
                    CppGeneratedArtifactKind.PublicHeader => "include/extensions",
                    CppGeneratedArtifactKind.NativeSource => "src/extensions",
                    _ => "resources/extensions"
                };
                string relativePath = Path.Combine(directory, artifact.relativePath).Replace('\\', '/');
                if (Path.IsPathRooted(artifact.relativePath) || artifact.relativePath.Split('/', '\\').Any(part => part == ".."))
                    throw new InvalidOperationException($"Lowering artifact '{artifact.relativePath}' escapes its contribution directory.");
                artifacts.Add(new(relativePath, [new(CppBridgeOperationKind.Fragment, artifact.content)], artifact.exposeToBindings && stage == CppGeneratedArtifactKind.PublicHeader));
            }
        }

        CppTypeLoweringRecipe[] projections = GetProjectionRecipes(config);
        if (projections.Length > 0)
        {
            StringBuilder header = new("#pragma once\n#include <stdint.h>\n#include <stddef.h>\n#include <stdbool.h>\n");
            foreach (CppTypeLoweringRecipe recipe in projections)
                header.Append("typedef ").Append(recipe.cAbiType).Append(' ').Append(GetProjectionAlias(recipe.name)).AppendLine(";");
            artifacts.Add(new("include/extensions/managed_projection_types.h", [new(CppBridgeOperationKind.Fragment, header.ToString())], true));
        }
        return artifacts.AsReadOnly();
    }

    /// <summary>
    /// Captures managed source contributions and conversions before emission begins.
    /// </summary>
    /// <param name="config">Attempt-local configuration and lowering contributors.</param>
    /// <returns>A frozen plan that retains neither configuration nor extension instances.</returns>
    /// <exception cref="InvalidOperationException">A contribution uses an invalid stage or repeated conversion identity.</exception>
    public static CppManagedArtifactPlan CollectManaged(Cpp2CGeneratorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        List<CppManagedSourceArtifact> sources = [];
        CppArtifactContext context = new(config, config.resolvedTarget.targetId.value, CppGeneratedArtifactKind.ManagedSource);
        foreach (CppGeneratedArtifact artifact in config.lowerings.CollectArtifacts(context))
        {
            if (artifact.kind != CppGeneratedArtifactKind.ManagedSource)
                throw new InvalidOperationException($"Artifact '{artifact.relativePath}' was returned for managed stage but declares {artifact.kind}.");
            if (!artifact.relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Managed lowering artifact '{artifact.relativePath}' must use the .cs extension.");
            sources.Add(new(artifact.relativePath, artifact.content));
        }
        CppManagedProjectionBinding[] projections = GetProjectionRecipes(config).Select(recipe => new CppManagedProjectionBinding(
            GetProjectionAlias(recipe.name), SanitizeIdentifier(recipe.name), recipe.managedProjection!.managedType,
            recipe.managedProjection.managedToNativeExpression, recipe.managedProjection.nativeToManagedExpression,
            recipe.managedProjection.requiredNamespace)).ToArray();
        return new(config.resolvedTarget.targetId.value, config.cSharpNamespace, SanitizeIdentifier(config.cSharpApiName), sources, projections);
    }

    /// <summary>
    /// Writes frozen extension sources and conversions into a caller-owned candidate directory.
    /// </summary>
    /// <param name="plan">Frozen managed contribution captured during bridge analysis.</param>
    /// <param name="bindings">C binding IR supplying the actual target ABI carriers.</param>
    /// <param name="context">Caller-owned candidate output and runtime namespace.</param>
    /// <returns>Written source paths; publication remains the caller's responsibility.</returns>
    /// <exception cref="ArgumentNullException">A required input is null.</exception>
    /// <exception cref="InvalidOperationException">Targets differ, carriers are unavailable, or output paths escape or repeat.</exception>
    public static IReadOnlyList<string> EmitManaged(
        CppManagedArtifactPlan plan,
        BindingModule bindings,
        EmissionContext context
    ) {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(context);
        if (plan.targetId != bindings.targetAbi)
            throw new InvalidOperationException($"Managed projection target '{plan.targetId}' differs from binding target '{bindings.targetAbi}'.");
        string root = Path.Combine(Path.GetFullPath(context.outputPath), "Extensions");
        List<(string path, string content)> outputs = [];
        foreach (CppManagedSourceArtifact source in plan.sources)
            outputs.Add((Path.GetFullPath(source.relativePath, root), source.content));
        if (plan.projections.Count > 0)
            outputs.Add((Path.Combine(root, "ConfiguredLoweringProjections.g.cs"), BuildConfiguredManagedProjections(plan, bindings, context.runtimeNamespace)));
        HashSet<string> paths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach ((string path, _) in outputs)
        {
            EnsureContained(root, path);
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !paths.Add(path))
                throw new InvalidOperationException($"Managed projection output path is invalid or repeated: '{path}'.");
        }
        foreach ((string path, string content) in outputs)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        return Array.AsReadOnly(outputs.Select(output => output.path).ToArray());
    }

    private static string BuildConfiguredManagedProjections(
        CppManagedArtifactPlan plan,
        BindingModule bindings,
        string runtimeNamespace
    ) {
        SortedSet<string> namespaces = new(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(runtimeNamespace))
            namespaces.Add(runtimeNamespace);
        foreach (CppManagedProjectionBinding projection in plan.projections)
            if (!string.IsNullOrWhiteSpace(projection.requiredNamespace))
                namespaces.Add(projection.requiredNamespace!);
        StringBuilder writer = new();
        writer.AppendLine("// <auto-generated/>");
        foreach (string value in namespaces)
            writer.Append("using ").Append(value).AppendLine(";");
        writer.Append("namespace ").Append(plan.namespaceName).AppendLine(";");
        writer.Append("public static unsafe partial class ").Append(plan.apiName).AppendLine("Lowerings");
        writer.AppendLine("{");
        foreach (CppManagedProjectionBinding projection in plan.projections)
        {
            BindingType? alias = bindings.types.SingleOrDefault(type => type.nativeName == projection.aliasName);
            string nativeType = alias?.underlyingType?.managedName
                ?? throw new InvalidOperationException($"C ABI carrier '{projection.aliasName}' is absent from analyzed bindings.");
            string methodName = projection.methodName;
            if (!string.IsNullOrWhiteSpace(projection.managedToNativeExpression))
            {
                writer.Append("    public static ").Append(nativeType).Append(" ToNative_").Append(methodName).Append('(').Append(projection.managedType).Append(" value) => ").Append(projection.managedToNativeExpression!.Replace("{value}", "value", StringComparison.Ordinal)).AppendLine(";");
            }

            if (!string.IsNullOrWhiteSpace(projection.nativeToManagedExpression))
            {
                writer.Append("    public static ").Append(projection.managedType).Append(" FromNative_").Append(methodName).Append('(').Append(nativeType).Append(" value) => ").Append(projection.nativeToManagedExpression!.Replace("{value}", "value", StringComparison.Ordinal)).AppendLine(";");
            }
        }

        writer.AppendLine("}");
        return writer.ToString();
    }

    private static CppTypeLoweringRecipe[] GetProjectionRecipes(Cpp2CGeneratorConfig config)
    {
        CppTypeLoweringRecipe[] recipes = config.typeLowerings.Where(recipe => recipe.managedProjection != null)
            .OrderBy(recipe => recipe.name, StringComparer.Ordinal).ToArray();
        HashSet<string> aliases = new(StringComparer.Ordinal);
        foreach (CppTypeLoweringRecipe recipe in recipes)
        {
            if (!aliases.Add(GetProjectionAlias(recipe.name)))
                throw new InvalidOperationException($"Managed lowering identity '{recipe.name}' repeats after identifier normalization.");
        }
        return recipes;
    }

    private static string GetProjectionAlias(string name) => "BGCS_Projection_" + SanitizeIdentifier(name);

    private static string SanitizeIdentifier(string value)
    {
        string result = string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_'));
        if (string.IsNullOrEmpty(result))
            return "Custom";
        return char.IsDigit(result[0]) ? "_" + result : result;
    }

    private static void EnsureContained(
        string root,
        string path
    ) {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"Generated extension path escapes its output root: {path}");
    }
}
