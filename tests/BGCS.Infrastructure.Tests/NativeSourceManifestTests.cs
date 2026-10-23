using System;
using System.Collections.Immutable;
using System.IO;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core;
using BGCS.Core.Calls;
using BGCS.Core.Native;
using BGCS.Core.Targets;
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
        NativeSourceManifest manifest = Manifest() with
        {
            inputs = [
            new("user", "Item.h", 1, s_digest, "Dependency"), new("user", "item.h", 1, s_digest, "Dependency"),
            new("user", "native.h", 1, s_digest, "EntryHeader")]
        };
        NativeSourceManifestVerifier.Validate(manifest, [s_source]);
    }

    [Fact]
    public void ExportBindingsKeepLogicalSourcesSeparateFromUniquePhysicalCallables()
    {
        NativeSourceManifest manifest = Manifest();
        var original = manifest.exportBindings[0];
        NativeSourceManifestVerifier.Validate(manifest with
        {
            exportBindings = [original,
            new(new("call:adapter"), original.sourceDeclaration, "compute_adapter", "test-library"),
            new(new("support:release"), null, "release_error", "test-library")]
        }, [s_source]);
        Assert.Throws<InvalidDataException>(() => NativeSourceManifestVerifier.Validate(manifest with { exportBindings = [original, original] }, [s_source]));
        Assert.Throws<InvalidDataException>(() => NativeSourceManifestVerifier.Validate(manifest with { exportBindings = [original with { moduleId = "another-module" }] }, [s_source]));
        Assert.Throws<InvalidDataException>(() => NativeSourceManifestVerifier.Validate(manifest with { schemaVersion = 1 }, [s_source]));
    }

    [Fact]
    public void RuntimeRequirementsMustBindTheExactTargetAndSelectedImportMechanism()
    {
        NativeSourceManifest manifest = Manifest();
        ManagedRuntimeProfile profile = new("fixture/execution-v1", new("fixture.runtime"), NativeModuleMechanism.ExternalFunctionTable,
            [BindingImportMode.FunctionTable], false, true)
        {
            nativeTargetId = manifest.targetId,
            nativeTriple = manifest.triple,
            pointerSize = 8,
            byteOrder = NativeByteOrder.LittleEndian,
            callAbi = new CallAbiId("fixture.abi")
        };
        NativeRuntimeRequirements requirement = new(profile, BindingImportMode.FunctionTable);
        NativeSourceManifestVerifier.Validate(manifest with { runtimeRequirements = requirement }, [s_source]);
        Assert.Throws<InvalidDataException>(() => NativeSourceManifestVerifier.Validate(manifest with { runtimeRequirements = requirement with { execution = profile with { nativeTargetId = "another-target" } } }, [s_source]));
        Assert.Throws<InvalidDataException>(() => NativeSourceManifestVerifier.Validate(manifest with { runtimeRequirements = requirement with { importMode = BindingImportMode.DllImport } }, [s_source]));
        Assert.Throws<InvalidDataException>(() => NativeSourceManifestVerifier.Validate(manifest with { runtimeRequirements = requirement with { execution = profile with { moduleMechanism = NativeModuleMechanism.StaticAdapter } } }, [s_source]));
    }

    private static NativeSourceManifest Manifest() => new(2, "test", "x86_64-pc-linux-gnu", SourceLanguage.C, "c17", [s_source], [],
        [new("user", "user"), new("generated", "generated")], [new("user", "native.h", 1, s_digest, "EntryHeader")],
        [new("user", "", false, false)], [new("user", "native.h", "native.h")], [], new("test-library", "test-native"),
        [new(new("call:compute"), new StableId("source:compute"), "compute", "test-library")], null, []);
}
