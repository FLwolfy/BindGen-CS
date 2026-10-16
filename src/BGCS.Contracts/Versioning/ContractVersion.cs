namespace BGCS.Contracts.Versioning;

/// <summary>Defines the neutral extension contract release accepted by the current product.</summary>
public static class ContractVersion
{
    /// <summary>The required Contracts major version; a different major is incompatible.</summary>
    public const int C_MAJOR = 3;
    /// <summary>The highest minimum Contracts API minor version this host implements.</summary>
    public const int C_API_MINOR = 0;
}
