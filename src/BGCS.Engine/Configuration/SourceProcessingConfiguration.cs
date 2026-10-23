using BGCS.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using BGCS.Composition;
using BGCS.Core.Ports;
using BGCS.CSharp.Processing;

namespace BGCS.Configuration;

/// <summary>Selects one declarative managed candidate operation without deserializing executable code.</summary>
public enum SourceProcessorKind
{
    /// <summary>Replaces exact text with an ordinal literal replacement.</summary>
    ReplaceText,
    /// <summary>Uses invariant, finite-budget .NET regex matching and dollar substitutions.</summary>
    RegexReplace,
    /// <summary>Adds parsed C# through the candidate owner and final managed layout.</summary>
    AddSource
}

/// <summary>Declares one immutable managed source operation before final ABI checking and requested compilation.</summary>
public sealed record SourceProcessorConfiguration
{
    /// <summary>Gets the globally unique stable processor identity.</summary>
    public required string id { get; init; }
    /// <summary>Gets descending execution priority followed by ordinal identity.</summary>
    public int priority { get; init; }
    /// <summary>Gets the explicit text, regex or additional-source operation.</summary>
    public SourceProcessorKind kind { get; init; }
    /// <summary>Gets case-sensitive portable managed-relative selectors for existing C# artifacts.</summary>
    public ImmutableArray<string> paths { get; init; } = ["**/*.cs"];
    /// <summary>Gets nonempty literal or regex text for an existing-source operation.</summary>
    public string? match { get; init; }
    /// <summary>Gets literal replacement text or .NET regex dollar substitutions.</summary>
    public string replacement { get; init; } = "";
    /// <summary>Gets the new portable managed-relative C# path for addSource.</summary>
    public string? path { get; init; }
    /// <summary>Gets the complete added C# unit for addSource.</summary>
    public string? content { get; init; }
    /// <summary>Gets ordinal literal matching or invariant regex case folding.</summary>
    public bool ignoreCase { get; init; }
    /// <summary>Gets regex multiline anchor semantics.</summary>
    public bool multiline { get; init; }
    /// <summary>Gets regex dot matching across line endings.</summary>
    public bool singleline { get; init; }
    /// <summary>Gets whether regex captures require explicit names.</summary>
    public bool explicitCapture { get; init; }
    /// <summary>Gets the finite total and matching regex budget per selected artifact, in milliseconds.</summary>
    public int timeoutMilliseconds { get; init; } = 1000;
    /// <summary>Gets whether an existing-source rule must match actual text rather than silently doing nothing.</summary>
    public bool requireMatch { get; init; } = true;
}

internal static class ConfiguredSourceProcessors
{
    internal static void Validate(OutputConfiguration output)
    {
        if (output.processors.IsDefault || output.processors.Length > 128) throw new ArgumentException("Managed source processors must be initialized and limited to 128 operations.");
        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> added = new(StringComparer.OrdinalIgnoreCase);
        long textLength = 0;
        foreach (SourceProcessorConfiguration rule in output.processors)
        {
            ArgumentNullException.ThrowIfNull(rule);
            if (!ids.Add(rule.id)) throw new ArgumentException("Managed source processor identities must be unique: " + rule.id);
            _ = Create(rule);
            textLength += (rule.content?.Length ?? 0) + (rule.match?.Length ?? 0) + rule.replacement.Length;
            if (textLength > 16 * 1024 * 1024) throw new ArgumentException("Inline managed source processing declarations exceed 16 MiB.");
            if (rule.kind == SourceProcessorKind.AddSource && (!added.Add(rule.path!)
                || added.Any(path => path != rule.path && (path.StartsWith(rule.path + "/", StringComparison.OrdinalIgnoreCase)
                    || rule.path!.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)))))
                throw new ArgumentException("Added source paths must not collide by case or overlap as files/directories.");
        }
    }

    internal static ImmutableArray<ServiceFactory<IArtifactProcessor>> Prepare(
        ImmutableArray<SourceProcessorConfiguration> rules,
        ImmutableArray<ServiceFactory<IArtifactProcessor>> registered
    ) {
        HashSet<string> ids = registered.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        List<ServiceFactory<IArtifactProcessor>> result = [.. registered];
        foreach (SourceProcessorConfiguration rule in rules)
        {
            if (!ids.Add(rule.id)) throw new ArgumentException("A configured source processor conflicts with a registered extension: " + rule.id);
            result.Add(new(new ExtensionDescriptor(rule.id, ExtensionPort.ArtifactProcessor, rule.priority), () => Create(rule)));
        }
        return result.OrderByDescending(item => item.Priority).ThenBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray();
    }

    private static IArtifactProcessor Create(SourceProcessorConfiguration rule)
    {
        if (!Enum.IsDefined(rule.kind) || rule.paths.IsDefault || rule.replacement is null)
            throw new ArgumentException("A source processor has an unknown operation or uninitialized immutable fields.");
        if (rule.kind == SourceProcessorKind.AddSource)
        {
            if (rule.match is not null || rule.replacement != "" || rule.ignoreCase || rule.multiline || rule.singleline
                || rule.explicitCapture || rule.timeoutMilliseconds != 1000 || !rule.requireMatch
                || !rule.paths.SequenceEqual(["**/*.cs"], StringComparer.Ordinal))
                throw new ArgumentException("addSource does not accept existing-text selectors, replacements or regex choices.");
            return new AdditionalSourceArtifactProcessor(rule.path!, rule.content!);
        }
        if (rule.path is not null || rule.content is not null) throw new ArgumentException("Existing-source rules do not accept added source paths or content.");
        if (rule.kind == SourceProcessorKind.ReplaceText)
        {
            if (rule.multiline || rule.singleline || rule.explicitCapture || rule.timeoutMilliseconds != 1000)
                throw new ArgumentException("A literal source replacement does not accept regex options or a regex budget.");
            return new TextArtifactProcessor(rule.paths, rule.match!, rule.replacement, rule.ignoreCase, rule.requireMatch);
        }
        RegexOptions options = RegexOptions.None;
        if (rule.ignoreCase) options |= RegexOptions.IgnoreCase;
        if (rule.multiline) options |= RegexOptions.Multiline;
        if (rule.singleline) options |= RegexOptions.Singleline;
        if (rule.explicitCapture) options |= RegexOptions.ExplicitCapture;
        return new RegexArtifactProcessor(rule.paths, rule.match!, rule.replacement, rule.timeoutMilliseconds, options, rule.requireMatch);
    }
}
