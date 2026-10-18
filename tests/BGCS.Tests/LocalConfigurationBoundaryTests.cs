using System;
using System.Text.Json;
using BGCS.Configuration;
using Xunit;

namespace BGCS.Tests;

public sealed class LocalConfigurationBoundaryTests
{
    [Theory]
    [InlineData("https://configs.example/base.json")]
    [InlineData("http://configs.example/base.json")]
    [InlineData("file:///base.json")]
    public void RemoteInheritanceHasNoProductTransport(string address)
    {
        Assert.Throws<ArgumentException>(() => ConfigurationReference.Local(address));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ConfigurationReference>(JsonSerializer.Serialize(address)));
    }

    [Fact]
    public void FormerPinnedRemoteObjectsAreRejectedRatherThanSilentlyIgnored()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ConfigurationReference>(
            """{"url":"https://configs.example/base.json","sha256":"abc","pathBase":"."}"""));
    }

    [Theory]
    [InlineData("base.json")]
    [InlineData("../shared/base.json")]
    public void LocalReferencesRoundTripWithoutChangingTheirDeclaringPath(string path)
    {
        ConfigurationReference reference = ConfigurationReference.Local(path);
        Assert.Equal(path, reference.path);
        Assert.Equal(path, JsonSerializer.Deserialize<ConfigurationReference>(JsonSerializer.Serialize(reference))!.path);
    }
}
