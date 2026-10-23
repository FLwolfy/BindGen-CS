using System;
using System.Security.Cryptography;
using System.Text.Json;
using BGCS.Core.Common;

namespace BGCS.Core.Targets;

/// <summary>Identifies the exact detached execution-capability contents without recognizing a runtime family.</summary>
public static class ManagedRuntimeProfileIdentity
{
    /// <summary>Hashes every current profile field in the contract's deterministic serialization order.</summary>
    /// <param name="profile">The immutable execution capabilities.</param>
    /// <returns>The content identity used to prevent profile changes after call assessment.</returns>
    public static ContentDigest Capture(ManagedRuntimeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new(Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(profile))));
    }
}
