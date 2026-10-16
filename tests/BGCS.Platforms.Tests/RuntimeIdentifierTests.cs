using BGCS.Contracts.Targets;
using BGCS.Platforms.Assets;
using Xunit;

namespace BGCS.Platforms.Tests;

public sealed class RuntimeIdentifierTests
{
    [Theory]
    [InlineData("linux-x64-gnu", "linux-x64")]
    [InlineData("linux-x64-musl", "linux-musl-x64")]
    [InlineData("linux-arm64-musl", "linux-musl-arm64")]
    public void NativeAssetRid_PreservesLibcAbi(string target, string expected)
        => Assert.Equal(expected, RuntimeIdentifierMapper.Map(new NativeTargetId(target)));
}
