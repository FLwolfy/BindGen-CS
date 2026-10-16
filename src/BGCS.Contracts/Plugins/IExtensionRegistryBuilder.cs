using System;

namespace BGCS.Contracts.Plugins;

/// <summary>Accepts session-owned factories for declared neutral extension ports.</summary>
public interface IExtensionRegistryBuilder
{
    /// <summary>Registers exactly one declared service without constructing or retaining it globally.</summary>
    /// <typeparam name="TService">An exact neutral extension interface.</typeparam>
    /// <param name="id">The service identity present in the owning plugin descriptor.</param>
    /// <param name="factory">The factory whose results are released by the generation session.</param>
    void RegisterFactory<TService>(
        string id,
        Func<TService> factory
    ) where TService : class;
}
