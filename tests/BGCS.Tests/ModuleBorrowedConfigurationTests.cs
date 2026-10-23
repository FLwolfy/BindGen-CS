using System;
using System.IO;
using BGCS.Configuration;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class ModuleBorrowedConfigurationTests
{
    [Theory]
    [InlineData(NativeLifetimeKind.Unknown)]
    [InlineData(NativeLifetimeKind.Call)]
    [InlineData(NativeLifetimeKind.Owner)]
    [InlineData(NativeLifetimeKind.Static)]
    public void AModuleBorrowMustNotGuessOrWidenTheNativeAddressLifetime(NativeLifetimeKind lifetime)
    {
        BindingConfiguration configuration = Configuration() with
        { semantics = new() { borrowedResults = [new() { function = "get", lifetime = lifetime }] } };
        Assert.Throws<ArgumentException>(() => new GenerationRequest(configuration, Path.GetTempPath()));
    }

    [Fact]
    public void NativeResultsCannotHaveDuplicateOrConflictingOwnershipAssertions()
    {
        BorrowedResultConfiguration borrowing = new() { function = "get", lifetime = NativeLifetimeKind.Module };
        Assert.Throws<ArgumentException>(() => new GenerationRequest(Configuration() with
        { semantics = new() { borrowedResults = [borrowing, borrowing] } }, Path.GetTempPath()));
        Assert.Throws<ArgumentException>(() => new GenerationRequest(Configuration() with
        { semantics = new() { borrowedResults = [borrowing], ownedResults = [new() { function = "get" }] } }, Path.GetTempPath()));
    }

    [Fact]
    public void ModuleBorrowedMembersCannotClaimAnOwnedReleaseDomain()
    {
        Assert.Throws<ArgumentException>(() => new GenerationRequest(Configuration() with
        {
            api = new() { memberFunctions = [new("read", "Widget", "self")
                { kind = ManagedReceiverKind.ModuleBorrowed, releaseDomain = "invented" }] }
        }, Path.GetTempPath()));
    }

    private static BindingConfiguration Configuration() => new()
    {
        schemaVersion = 2, library = new() { id = "borrowed", nativeName = "borrowed" }, source = new() { headers = ["native.h"] }
    };
}
