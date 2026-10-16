using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;

namespace BGCS;

/// <summary>Requires candidate compilation and declared native validation before formal publication.</summary>
/// <param name="generation">The frozen grouped source generation request.</param>
public sealed record BuildRequest(GenerationRequest generation)
{
    /// <summary>Reads a new-format configuration and captures its path base.</summary>
    /// <param name="path">The local bindgen.json input.</param>
    /// <returns>The candidate build request.</returns>
    public static BuildRequest FromFile(string path) => new(GenerationRequest.FromFile(path));

    /// <summary>Asynchronously reads a configuration closure, including explicitly pinned HTTPS bases.</summary>
    /// <param name="path">The local bindgen.json input.</param>
    /// <param name="profile">An optional preset selection.</param>
    /// <param name="cancellationToken">Cancels local and remote configuration reads.</param>
    /// <param name="configurationLoader">An optional loader with a caller-owned transport.</param>
    /// <returns>The immutable candidate build request.</returns>
    public static async Task<BuildRequest> FromFileAsync(
        string path,
        string? profile = null,
        CancellationToken cancellationToken = default,
        BindingConfigurationLoader? configurationLoader = null
    )
        => new(await GenerationRequest.FromFileAsync(path, profile, cancellationToken, configurationLoader).ConfigureAwait(false));
}
