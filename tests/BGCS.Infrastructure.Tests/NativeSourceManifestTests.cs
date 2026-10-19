using System;
using System.Collections.Immutable;
using System.IO;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.Verification;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class NativeSourceManifestTests
{
    private static readonly ContentDigest s_digest = new(new string('a', 64));
    private static readonly ArtifactDescriptor s_source = new("test/native/src/bridge.c", "text/x-c", s_digest);

    [Fact]
    public void CompleteManifestAcceptsPortableFilesWithoutPhysicalBindings()
        => NativeSourceManifestVerifier.Validate(Manifest(), [s_source]);

    [Theory]
    [InlineData("../escape.h")]
    [InlineData("/absolute.h")]
    [InlineData("C:/absolute.h")]
    [InlineData("folder\\native.h")]
    public void NonportableInputPathsAreRejected(string path)
    {
        NativeSourceManifest manifest = Manifest() with { inputs = [new("user", path, 1, s_digest, "EntryHeader")] };
        Assert.ThrowsAny<Exception>(() => NativeSourceManifestVerifier.Validate(manifest, [s_source]));
    }

    [Fact]
    public void MissingRootsAndOmittedOrStaleArtifactsAreRejected()
    {
        NativeSourceManifest manifest = Manifest();
        Assert.Throws<InvalidDataException>(() => NativeSourceManifestVerifier.Validate(manifest with { inputRoots = [new("generated", "generated")] }, [s_source]));
        Assert.Throws<InvalidDataException>(() => NativeSourceManifestVerifier.Validate(manifest with { sources = [] }, [s_source]));
        Assert.Throws<InvalidDataException>(() => NativeSourceManifestVerifier.Validate(manifest, [s_source with { digest = new(new string('b', 64)) }]));
    }

    [Fact]
    public void InputIdentitiesRetainCaseIndependentlyOfGeneratedArtifactCollisionPolicy()
    {
        NativeSourceManifest manifest = Manifest() with { inputs = [
            new("user", "Item.h", 1, s_digest, "Dependency"), new("user", "item.h", 1, s_digest, "Dependency"),
            new("user", "native.h", 1, s_digest, "EntryHeader")] };
        NativeSourceManifestVerifier.Validate(manifest, [s_source]);
    }

    private static NativeSourceManifest Manifest() => new(1, "test", "x86_64-pc-linux-gnu", SourceLanguage.C, "c17", [s_source], [],
        [new("user", "user"), new("generated", "generated")], [new("user", "native.h", 1, s_digest, "EntryHeader")],
        [new("user", "", false, false)], [new("user", "native.h", "native.h")], [], ["compute"], []);
}
