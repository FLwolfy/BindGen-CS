using System.Collections.Immutable;

namespace BGCS.Configuration;

/// <summary>Projects a native record into an existing closed consumer value type by copying its fields.</summary>
public sealed record ExternalTypeConfiguration
{
    /// <summary>Gets an exact native record identity, qualified name or unambiguous declaration name.</summary>
    public string nativeType { get; init; } = string.Empty;
    /// <summary>Gets the closed C# consumer type, including explicit generic arguments.</summary>
    public string managedType { get; init; } = string.Empty;
    /// <summary>Gets every ordinary native field mapped to one public writable consumer field.</summary>
    public ImmutableDictionary<string, string> fields { get; init; } = ImmutableDictionary<string, string>.Empty;
}
