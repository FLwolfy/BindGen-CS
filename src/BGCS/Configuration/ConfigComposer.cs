using System;
using BGCS.Core.Collections;
using BGCS.Core.Configuration;
using Newtonsoft.Json.Linq;

namespace BGCS.Configuration;

/// <summary>
/// Composes the declarative base chain of an in-memory configuration before runtime extensions are registered.
/// </summary>
public sealed class ConfigComposer : IConfigComposer
{
    /// <inheritdoc />
    public void Compose(
        ref CsCodeGeneratorConfig config,
        string baseDirectory
    ) {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        if (config.baseConfig?.url == null)
        {
            CollectionNormalizer.Normalize(config);
            return;
        }
        JObject document = JsonConfigurationComposer.Compose(JObject.Parse(config.Serialize()), baseDirectory);
        config = document.ToObject<CsCodeGeneratorConfig>()
            ?? throw new InvalidOperationException("Unable to deserialize the composed configuration.");
        CollectionNormalizer.Normalize(config);
    }
}
