namespace BGCS.Core.Hosting;

/// <summary>Creates explicit session-owned host facilities independently of native target selection.</summary>
public interface IHostServicesProvider
{
    /// <summary>Creates host facilities using only the supplied bootstrap request and host observations.</summary>
    /// <param name="request">Validated detached settings and an explicit path base.</param>
    /// <returns>The owned host service binding.</returns>
    HostServices Create(HostServicesRequest request);
}
