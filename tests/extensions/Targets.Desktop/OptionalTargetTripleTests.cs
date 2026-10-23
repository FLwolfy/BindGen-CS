using System;
using BGCS.Core.Inputs;
using BGCS.Core.Targets;
using BGCS.Extension.FreeBsd;
using Xunit;

namespace BGCS.Targets.Desktop.Tests;

public sealed class OptionalTargetTripleTests
{
    [Theory]
    [InlineData("x86_64-unknown-freebsd14.2")]
    [InlineData("x86_64-privatevendor-freebsd15.0")]
    public void ExplicitFreeBsdProviderPreservesCompatibleVersionedTriplesWithoutClaimingAManagedRuntime(string triple)
    {
        Assert.True(new FreeBsdTargetProvider().TryResolve(new(new("freebsd-x64-gnu"), tripleOverride: triple) { language = SourceLanguage.C }, out NativeTargetDescriptor? target));
        Assert.Equal(triple, target!.triple);
        Assert.Equal("freebsd", target.platformId);
        Assert.Equal("gnu", target.abiId);
        Assert.Equal("X86_64SysV", target.sourceConventions!.callableAssertions.defaultCConvention);
        Assert.Throws<ArgumentException>(() => new FreeBsdTargetProvider().TryResolve(new(new("freebsd-x64-gnu"), tripleOverride: "x86_64-privatevendor-linux-gnu"), out _));
    }
}
