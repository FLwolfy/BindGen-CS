using System;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Platforms;
using BGCS.Platforms.Assets;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeReferenceTests
{
    [Fact]
    public void VersionedDynamicModuleUsesItsVerifiedOutputFormAndOpaqueTargetFacts()
    {
        NativeTargetDescriptor builtin = new TargetResolver().Resolve(new(new("linux-x64-gnu")));
        NativeTargetDescriptor opaque = new(new("vendor-linux-versioned-module"), builtin.platformId, builtin.architectureId, builtin.abiId,
            builtin.triple, builtin.toolchain, builtin.capabilities, builtin.byteOrder);
        ArtifactDescriptor artifact = new("vendor/native/bin/libfoo.so.1", "application/octet-stream", new(new string('a', 64)));
        NativeReferenceDescriptor reference = NativeReferenceCatalog.Create(opaque, ManagedRuntimeResolver.Resolve(opaque, ManagedRuntimeKind.CoreClr), NativeOutputKind.SharedLibrary, artifact, "foo");
        Assert.Equal(NativeReferenceKind.DynamicCopy, reference.kind);
        Assert.Equal(artifact.relativePath, reference.relativePath);
        Assert.Equal(artifact.digest, reference.digest);
        Assert.Same(opaque, reference.target);
        Assert.Equal("linux-x64", reference.runtimeIdentifier);
        Assert.Equal("foo", reference.importModuleName);
    }

    [Fact]
    public void StaticDesktopConsumesNativeAotLibraryAndRejectsCoreClrProfile()
    {
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new("windows-x64-msvc")));
        Assert.Throws<NotSupportedException>(() => ManagedRuntimeResolver.Resolve(target, ManagedRuntimeKind.CoreClr, NativeOutputKind.StaticLibrary));
        ManagedRuntimeProfile profile = ManagedRuntimeResolver.Resolve(target, ManagedRuntimeKind.NativeAot, NativeOutputKind.StaticLibrary);
        Assert.Equal(NativeModuleMechanism.StaticAdapter, profile.moduleMechanism);
        ArtifactDescriptor artifact = new("target/native/bin/fixture.lib", "application/octet-stream", new(new string('b', 64)));
        NativeReferenceDescriptor reference = NativeReferenceCatalog.Create(target, profile, NativeOutputKind.StaticLibrary, artifact, "fixture");
        Assert.Equal(NativeReferenceKind.NativeAotLibrary, reference.kind);
        Assert.Equal("__Internal", reference.importModuleName);
    }

    [Theory]
    [InlineData("emscripten-wasm32-emscripten", ManagedRuntimeKind.BrowserWasm, NativeReferenceKind.BrowserNativeFile)]
    [InlineData("ios-arm64-darwin", ManagedRuntimeKind.NativeAot, NativeReferenceKind.AppleStatic)]
    [InlineData("ios-simulator-arm64-darwin", ManagedRuntimeKind.NativeAot, NativeReferenceKind.AppleStatic)]
    public void StaticApplicationReferenceUsesResolvedRuntimeAndPlatform(string targetId, ManagedRuntimeKind runtime, NativeReferenceKind expected)
    {
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new(targetId)));
        ArtifactDescriptor artifact = new("target/native/bin/archive.a", "application/octet-stream", new(new string('c', 64)));
        NativeReferenceDescriptor reference = NativeReferenceCatalog.Create(target, ManagedRuntimeResolver.Resolve(target, runtime), NativeOutputKind.StaticLibrary, artifact, "configured");
        Assert.Equal(expected, reference.kind);
        Assert.Equal(target.targetId, reference.target.targetId);
        Assert.Equal(expected == NativeReferenceKind.BrowserNativeFile ? "archive" : "__Internal", reference.importModuleName);
    }

    [Theory]
    [InlineData(NativeOutputKind.StaticLibrary, "libapi.a", "libapi")]
    [InlineData(NativeOutputKind.Object, "api.o", "api")]
    public void BrowserArtifactImportIdentityMatchesDeclaredNativeFileRegistration(NativeOutputKind kind, string filename, string module)
    {
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new("emscripten-wasm32-emscripten")));
        ManagedRuntimeProfile runtime = ManagedRuntimeResolver.Resolve(target, ManagedRuntimeKind.BrowserWasm, kind);
        Assert.Equal(filename, NativeOutputLayout.ResolveFilename("api", target, kind));
        Assert.Equal(module, NativeOutputLayout.ResolveImportModuleName(target, runtime, "api", filename));
        ArtifactDescriptor artifact = new("target/native/bin/" + filename, "application/octet-stream", new(new string('d', 64)));
        Assert.Equal(module, NativeReferenceCatalog.Create(target, runtime, kind, artifact, "api").importModuleName);
        Assert.Equal("api", NativeOutputLayout.ResolveImportModuleName(target, runtime, "api"));
    }

    [Fact]
    public void BrowserImportLayoutRejectsDirectoryAmbiguityAndForeignRuntimeFacts()
    {
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new("emscripten-wasm32-emscripten")));
        ManagedRuntimeProfile runtime = ManagedRuntimeResolver.Resolve(target, ManagedRuntimeKind.BrowserWasm);
        Assert.Throws<ArgumentException>(() => NativeOutputLayout.ResolveImportModuleName(target, runtime, "api", "elsewhere/libapi.a"));
        NativeTargetDescriptor desktop = new TargetResolver().Resolve(new(new("windows-x64-msvc")));
        Assert.Throws<NotSupportedException>(() => NativeOutputLayout.ResolveImportModuleName(desktop, runtime, "api", "libapi.a"));
    }

    [Fact]
    public void DynamicImportKeepsExplicitUnicodeAndPathIdentityWithoutManufacturingAnOutputFilename()
    {
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new("windows-x64-msvc")));
        ManagedRuntimeProfile runtime = ManagedRuntimeResolver.Resolve(target, ManagedRuntimeKind.CoreClr);
        const string importName = "C:/原生库/My Module.dll";
        Assert.Equal(importName, NativeOutputLayout.ResolveImportModuleName(target, runtime, importName));
        Assert.Throws<ArgumentException>(() => NativeOutputLayout.ResolveFilename(importName, target, NativeOutputKind.SharedLibrary));
    }
}
