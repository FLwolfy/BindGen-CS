using System;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Threading;
using BGCS.Contracts.Inputs;

namespace BGCS.Contracts.Plugins;

/// <summary>Declares local input dependencies before a target, plugin registry or extension result is produced.</summary>
public interface IExternalInputProvider
{
    /// <summary>Declares complete file, directory and existence dependencies without evaluating the extension's affected operation.</summary>
    /// <param name="context">The frozen settings and explicit input path base.</param>
    /// <param name="cancellationToken">Cancels input discovery.</param>
    /// <returns>The complete immutable local dependency requests.</returns>
    ImmutableArray<DeclaredExternalInput> GetExternalInputs(
        ExternalInputContext context,
        CancellationToken cancellationToken
    );
}

/// <summary>Captures input ownership before target resolution and plugin configuration.</summary>
public sealed record ExternalInputContext
{
    /// <summary>Clones settings and requires an absolute input base independent of a process working directory.</summary>
    /// <param name="configuration">The frozen serialized request settings.</param>
    /// <param name="pathBase">The absolute local input base.</param>
    public ExternalInputContext(
        JsonElement configuration,
        string pathBase
    ) {
        if (configuration.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("External input settings must be a complete JSON value.", nameof(configuration));
        ArgumentException.ThrowIfNullOrWhiteSpace(pathBase);
        if (!Path.IsPathFullyQualified(pathBase)) throw new ArgumentException("External input discovery requires an absolute path base.", nameof(pathBase));
        this.configuration = configuration.Clone();
        this.pathBase = pathBase;
    }

    /// <summary>Gets independently owned frozen serialized settings.</summary>
    public JsonElement configuration { get; }
    /// <summary>Gets the captured absolute local input base.</summary>
    public string pathBase { get; }
}
