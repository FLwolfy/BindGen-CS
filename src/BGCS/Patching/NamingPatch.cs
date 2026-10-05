using System;
using BGCS.Analysis;
using BGCS.Configuration;
using BGCS.Conversion;
using BGCS.CppAst.Model.Declarations;
using BGCS.CSharp;

namespace BGCS.Patching;

/// <summary>
/// Controls repeated prefix removal, ordinal case matching, and replacement of established managed names.
/// </summary>
public enum NamingPatchOptions
{
    /// <summary>
    /// Removes at most one prefix without replacing established friendly names.
    /// </summary>
    None,
    /// <summary>
    /// Allows removal of more than one configured prefix.
    /// </summary>
    MultiplePrefixes = 1 << 0,
    /// <summary>
    /// Replaces established friendly-name mappings.
    /// </summary>
    OverwriteNames = 1 << 1,
    /// <summary>
    /// Matches prefixes using ordinal case-insensitive comparison.
    /// </summary>
    CaseInsensitive = 1 << 2
}

/// <summary>
/// Selects the native declaration categories whose managed naming mappings may be changed.
/// </summary>
public enum NamingPatchMode
{
    /// <summary>
    /// Leaves every declaration category unchanged.
    /// </summary>
    None = 0,
    /// <summary>
    /// Applies prefix removal to callable names and aliases.
    /// </summary>
    Functions = 1 << 0,
    /// <summary>
    /// Applies prefix removal to record names.
    /// </summary>
    Structs = 1 << 1,
    /// <summary>
    /// Applies prefix removal to opaque handle names.
    /// </summary>
    Handles = 1 << 2,
    /// <summary>
    /// Applies prefix removal to enumeration names.
    /// </summary>
    Enums = 1 << 3,
    /// <summary>
    /// Applies prefix removal to every supported declaration category.
    /// </summary>
    All = Functions | Structs | Handles | Enums,
}

/// <summary>
/// Contributes managed name mappings by removing configured prefixes without rewriting native declarations.
/// </summary>
public class NamingPatch : PrePatch
{
    private readonly string[] m_prefixes;
    private readonly NamingPatchOptions m_options;
    private readonly NamingPatchMode m_mode;
    /// <summary>
    /// Captures prefix-removal policy for selected declaration categories.
    /// </summary>
    /// <param name="prefixes">
    /// Prefixes tried in the supplied order; the caller must not mutate the retained array during use.
    /// </param>
    /// <param name="options">
    /// Prefix matching and existing-mapping overwrite policy.
    /// </param>
    /// <param name="mode">
    /// Declaration categories to rename; function aliases follow the Functions flag.
    /// </param>
    public NamingPatch(
        string[] prefixes,
        NamingPatchOptions options,
        NamingPatchMode mode = NamingPatchMode.Functions
    ) {
        this.m_prefixes = prefixes;
        this.m_options = options;
        this.m_mode = mode;
    }

    /// <inheritdoc/>
    protected override void PatchCompilation(
        CsCodeGeneratorConfig config,
        ParseResult result
    ) {
        base.PatchCompilation(config, result);
        foreach (var aliases in result.functionAliases)
        {
            foreach (var alias in aliases.Value)
            {
                PatchAlias(config, alias);
            }
        }
    }

    /// <summary>
    /// Applies configured managed naming conventions to a native function alias.
    /// </summary>
    /// <param name="config">Generation naming configuration.</param>
    /// <param name="alias">Mutable attempt-local alias whose managed name is updated.</param>
    protected virtual void PatchAlias(
        CsCodeGeneratorConfig config,
        FunctionAlias alias
    ) {
        if ((this.m_mode & NamingPatchMode.Functions) == 0)
            return;
        var name = config.GetCsFunctionName(alias.exportedAliasName);
        name = Process(name);
        if (!config.TryGetFunctionAliasMapping(alias.exportedName, alias.exportedAliasName, out var mapping))
        {
            mapping = new(alias.exportedName, alias.exportedAliasName, name, null);
            config.AddFunctionAliasMapping(mapping);
        }

        if ((this.m_options & NamingPatchOptions.OverwriteNames) != 0)
        {
            mapping.friendlyName = name;
        }
        else
        {
            mapping.friendlyName ??= name;
        }
    }

    /// <inheritdoc/>
    protected override void PatchEnum(
        CsCodeGeneratorConfig config,
        CppEnum cppEnum
    ) {
        if ((this.m_mode & NamingPatchMode.Enums) == 0)
            return;
        var name = config.GetCsCleanName(cppEnum.name);
        name = Process(name);
        if (!config.TryGetEnumMapping(cppEnum.name, out var mapping))
        {
            mapping = new(cppEnum.name, name, null);
            config.enumMappings.Add(mapping);
            config.typeMappings[cppEnum.name] = name;
        }

        if ((this.m_options & NamingPatchOptions.OverwriteNames) != 0)
        {
            mapping.friendlyName = name;
        }
        else
        {
            mapping.friendlyName ??= name;
        }
    }

    /// <inheritdoc/>
    protected override void PatchClass(
        CsCodeGeneratorConfig config,
        CppClass cppClass
    ) {
        if ((this.m_mode & NamingPatchMode.Structs) == 0)
            return;
        var name = config.GetCsCleanName(cppClass.name);
        name = Process(name);
        if (!config.TryGetTypeMapping(cppClass.name, out var mapping))
        {
            mapping = new(cppClass.name, name, null);
            config.classMappings.Add(mapping);
            config.typeMappings.TryAdd(cppClass.name, name);
        }

        if ((this.m_options & NamingPatchOptions.OverwriteNames) != 0)
        {
            mapping.friendlyName = name;
        }
        else
        {
            mapping.friendlyName ??= name;
        }
    }

    /// <inheritdoc/>
    protected override void PatchTypedef(
        CsCodeGeneratorConfig config,
        CppTypedef cppTypedef
    ) {
        if ((this.m_mode & NamingPatchMode.Handles) == 0)
            return;
        if (!cppTypedef.IsOpaqueHandle())
            return;
        var name = config.GetCsTypeName(cppTypedef);
        name = Process(name);
        if (!config.TryGetHandleMapping(cppTypedef.name, out var mapping))
        {
            mapping = new(cppTypedef.name, name, null);
            config.handleMappings.Add(mapping);
            config.typeMappings.TryAdd(cppTypedef.name, name);
        }

        if ((this.m_options & NamingPatchOptions.OverwriteNames) != 0)
        {
            mapping.friendlyName = name;
        }
        else
        {
            mapping.friendlyName ??= name;
        }
    }

    /// <inheritdoc/>
    protected override void PatchFunction(
        CsCodeGeneratorConfig config,
        CppFunction cppFunction
    ) {
        if ((this.m_mode & NamingPatchMode.Functions) == 0)
            return;
        var name = config.GetCsFunctionName(cppFunction.name);
        name = Process(name);
        if (!config.TryGetFunctionMapping(cppFunction.name, out var mapping))
        {
            mapping = new(cppFunction.name, name, null, [], []);
            config.functionMappings.Add(mapping);
        }

        if ((this.m_options & NamingPatchOptions.OverwriteNames) != 0)
        {
            mapping.friendlyName = name;
        }
        else
        {
            mapping.friendlyName ??= name;
        }
    }

    private string Process(string name)
    {
        bool changed = false;
        foreach (var prefix in this.m_prefixes)
        {
            if (name.StartsWith(prefix, (this.m_options & NamingPatchOptions.CaseInsensitive) != 0 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                changed = true;
                name = name[prefix.Length..];
                if (!this.m_options.HasFlag(NamingPatchOptions.MultiplePrefixes))
                {
                    break;
                }
            }
        }

        if (changed && name.Length > 0)
            name = string.Concat(char.ToUpperInvariant(name[0]).ToString(), name.AsSpan(1));

        return name;
    }
}
