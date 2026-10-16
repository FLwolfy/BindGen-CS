using System;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Threading;
using BGCS.Contracts;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.Workers;
using BGCS.Platforms;
using BGCS.Platforms.Capabilities;
using BGCS.Platforms.Assets;
using BGCS.NativeBuild.Planning;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class TargetCapabilityTests
{
    [Theory]
    [InlineData("android")]
    [InlineData("ios")]
    [InlineData("ios-simulator")]
    [InlineData("emscripten")]
    [InlineData("windows")]
    public void GnuRejectsUnsupportedTargetFamiliesBeforeConstructingAPlan(string family)
    {
        string scratch = Path.GetTempPath();
        NativeTargetDescriptor target = new(new("unverified-" + family), family, "x64", "gnu", "x86_64-unknown-" + family, new());
        NativeBuildRequest request = new(target, [new(Path.Combine(scratch, "do-not-open-native-source.c"), BGCS.Contracts.Inputs.SourceLanguage.C, "c17")], scratch,
            Path.Combine(scratch, "do-not-create-native-output.o")) { outputKind = NativeOutputKind.Object };
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() => new CompilerBuildProvider("gnu", "do-not-start-g++").Plan(request, CancellationToken.None));
        Assert.Contains("GNU native construction currently supports", failure.Message);
    }

    [Fact]
    public void CustomTargetCarriesNeutralCapabilitiesThroughWorkerSerializationAndRuntimeResolution()
    {
        TargetCapabilities declared = new([NativeOutputKind.SharedLibrary, NativeOutputKind.StaticLibrary], NativeOutputKind.SharedLibrary, true, true, "vendor-native");
        NativeTargetDescriptor target = new(new("vendor-riscv64-custom"), "vendor", "riscv64", "custom", "riscv64-unknown-vendor", new(), declared);
        string json = JsonSerializer.Serialize(target, WorkerProtocol.jsonOptions);
        NativeTargetDescriptor restored = JsonSerializer.Deserialize<NativeTargetDescriptor>(json, WorkerProtocol.jsonOptions)!;
        Assert.Equal("vendor-native", PlatformCapabilityCatalog.Get(restored).assetLayout);
        Assert.Equal<NativeOutputKind>(declared.outputKinds, restored.capabilities!.outputKinds);
        ManagedRuntimeProfile runtime = ManagedRuntimeResolver.Resolve(restored, ManagedRuntimeKind.CoreClr);
        Assert.Equal(NativeModuleMechanism.DynamicLibrary, runtime.moduleMechanism);
        Assert.Contains(BindingImportMode.FunctionTable, runtime.importModes);
    }

    [Fact]
    public void ContradictoryCustomCapabilitiesAreRejectedInsteadOfInventingPlatformSupport()
    {
        NativeTargetDescriptor missing = new(new("vendor-riscv64-custom"), "vendor", "riscv64", "custom", "riscv64-unknown-vendor", new());
        Assert.Throws<NotSupportedException>(() => PlatformCapabilityCatalog.Get(missing));
        NativeTargetDescriptor inconsistent = new(missing.targetId, missing.platformId, missing.architectureId, missing.abiId, missing.triple, missing.toolchain,
            new([NativeOutputKind.StaticLibrary], NativeOutputKind.SharedLibrary, true, false, "vendor-native"));
        Assert.Throws<InvalidOperationException>(() => PlatformCapabilityCatalog.Get(inconsistent));
    }

    [Fact]
    public void OpaqueTargetIdsMapOnlyFromKnownPlatformCpuAndAbiFacts()
    {
        NativeTargetDescriptor gnu = new(new("custom-linux-target"), "linux", "x64", "gnu", "x86_64-unknown-linux-gnu", new());
        NativeTargetDescriptor musl = new(gnu.targetId, gnu.platformId, gnu.architectureId, "musl", "x86_64-unknown-linux-musl", gnu.toolchain);
        Assert.Equal("linux-x64", RuntimeIdentifierMapper.Map(gnu));
        Assert.Equal("linux-musl-x64", RuntimeIdentifierMapper.Map(musl));
        NativeTargetDescriptor unknown = new(gnu.targetId, gnu.platformId, gnu.architectureId, "unknown", gnu.triple, gnu.toolchain);
        Assert.Throws<NotSupportedException>(() => RuntimeIdentifierMapper.Map(unknown));
    }
}
