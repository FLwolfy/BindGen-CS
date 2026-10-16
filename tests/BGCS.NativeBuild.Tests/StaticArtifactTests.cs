using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Contracts.Native;
using BGCS.NativeBuild.Execution;
using BGCS.NativeBuild.Packaging;
using BGCS.NativeBuild.Planning;
using BGCS.NativeBuild.Validation;
using BGCS.Infrastructure.Execution;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using BGCS.Platforms.Assets;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class StaticArtifactTests
{
    [Theory]
    [InlineData(NativeOutputKind.StaticLibrary)]
    [InlineData(NativeOutputKind.Object)]
    public async Task MsvcConstructsAndVerifiesStaticAssets(NativeOutputKind kind)
    {
        if (!OperatingSystem.IsWindows()) return;
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs static & " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string compiler = MsvcToolchainDiscovery.FindCompiler("x64") ?? throw new InvalidOperationException("MSVC is required by the Windows static native gate.");
            NativeTargetDescriptor target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("windows-x64-msvc"), new(compiler))), SourceLanguage.C);
            string source = Path.Combine(temporary, "source.c");
            await File.WriteAllTextAsync(source, "int static_roundtrip(int value) { return value + 1; }\n");
            string scratch = Path.Combine(temporary, "scratch");
            Directory.CreateDirectory(scratch);
            NativeBuildRequest request = new(target, [new(source, SourceLanguage.C, "c17")], scratch, Path.Combine(scratch, kind == NativeOutputKind.StaticLibrary ? "fixture.lib" : "fixture.obj")) { outputKind = kind };
            NativeBuildPlan plan = new NativeBuildProviderResolver().Resolve(target, "msvc").Plan(request, CancellationToken.None);
            await new PlannedNativeBuildExecutor().ExecuteAsync(plan, TimeSpan.FromMinutes(2));
            NativeArtifactVerifier.Verify(plan.outputFile, target.targetId.value, kind);
            var exports = await NativeExportInspector.InspectAsync(target.targetId.value, plan.outputFile, ["static_roundtrip"], outputKind: kind);
            Assert.True(exports.success);
            NativeTargetDescriptor opaque = new(new("vendor-specialized-native-assets"), target.platformId, target.architectureId, target.abiId, target.triple, target.toolchain);
            NativeArtifactVerifier.Verify(plan.outputFile, opaque, kind);
            Assert.True((await NativeExportInspector.InspectAsync(opaque, plan.outputFile, ["static_roundtrip"], outputKind: kind)).success);
            byte[] nativeBytes = await File.ReadAllBytesAsync(plan.outputFile);
            ContentDigest digest = new(Convert.ToHexStringLower(SHA256.HashData(nativeBytes)));
            NativeAssetPackageResult packaged = await new NativeAssetPackager().PackageAsync(new([new(plan.outputFile, opaque.targetId, kind, digest) { target = opaque }], new CaptureSink()), CancellationToken.None);
            NativePackageAsset asset = Assert.Single(packaged.assets);
            Assert.Equal(opaque.targetId, asset.targetId);
            Assert.Same(opaque, asset.target);
            Assert.Equal("win-x64", asset.runtimeIdentifier);
            Assert.Equal("NativeLibrary", asset.buildItem);
            await Assert.ThrowsAsync<ArgumentException>(async () => await new NativeAssetPackager().PackageAsync(new([new(plan.outputFile, target.targetId, kind, digest) { target = opaque }], new CaptureSink()), CancellationToken.None));
            Assert.Throws<InvalidDataException>(() => NativeArtifactVerifier.Verify(plan.outputFile, "windows-arm64-msvc", kind));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task WasmArchiveIsPackagedAsNativeFileReferenceAndNeverDesktopRid()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-wasm-asset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string path = Path.Combine(temporary, "fixture.a");
            await File.WriteAllBytesAsync(path, Archive(WasmObject()));
            ContentDigest digest = new(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))));
            CaptureSink sink = new();
            NativeAssetPackageResult result = await new NativeAssetPackager().PackageAsync(new([new(path, new("emscripten-wasm32-emscripten"), NativeOutputKind.StaticLibrary, digest)], sink), CancellationToken.None);
            NativePackageAsset asset = Assert.Single(result.assets);
            Assert.Equal("browser-wasm", asset.runtimeIdentifier);
            Assert.Equal("NativeFileReference", asset.buildItem);
            Assert.StartsWith("buildTransitive/native/emscripten-wasm32-emscripten/", asset.relativePath);
            Assert.Contains("NativeFileReference", Encoding.UTF8.GetString(sink.files["buildTransitive/BGCS.NativeAssets.targets"]));
            Assert.DoesNotContain(sink.files.Keys, key => key.StartsWith("runtimes/", StringComparison.Ordinal));
            await File.WriteAllBytesAsync(path, Archive(Coff(0x8664)));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await new NativeAssetPackager().PackageAsync(new([new(path, new("emscripten-wasm32-emscripten"), NativeOutputKind.StaticLibrary, digest)], new CaptureSink()), CancellationToken.None));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void ElfAndArchiveByteOrderMustMatchTheIndependentTargetFact()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-native-byte-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string path = Path.Combine(temporary, "fixture");
            NativeTargetDescriptor little = new TargetResolver().Resolve(new(new("linux-arm64-gnu")));
            NativeTargetDescriptor big = new(new("vendor-big-endian-arm64"), "linux", "arm64", "gnu", "aarch64_be-unknown-linux-gnu", new(), byteOrder: NativeByteOrder.BigEndian);
            Assert.Contains("big-endian", Assert.Throws<NotSupportedException>(() => RuntimeIdentifierMapper.Map(big)).Message);
            Assert.Equal("linux-arm64", RuntimeIdentifierMapper.Map(little));
            foreach (NativeOutputKind kind in new[] { NativeOutputKind.Object, NativeOutputKind.SharedLibrary, NativeOutputKind.StaticLibrary })
            {
                byte[] elf = Elf(bigEndian: true, relocatable: kind != NativeOutputKind.SharedLibrary);
                File.WriteAllBytes(path, kind == NativeOutputKind.StaticLibrary ? Archive(elf) : elf);
                NativeArtifactVerifier.Verify(path, big, kind);
                Assert.Contains("byte order", Assert.Throws<InvalidDataException>(() => NativeArtifactVerifier.Verify(path, little, kind)).Message);
                elf = Elf(bigEndian: false, relocatable: kind != NativeOutputKind.SharedLibrary);
                File.WriteAllBytes(path, kind == NativeOutputKind.StaticLibrary ? Archive(elf) : elf);
                NativeArtifactVerifier.Verify(path, little, kind);
                Assert.Contains("byte order", Assert.Throws<InvalidDataException>(() => NativeArtifactVerifier.Verify(path, big, kind)).Message);
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UniversalMachOUsesSelectedSliceByteOrderRatherThanFatTableByteOrder(bool wide)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-fat-byte-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string path = Path.Combine(temporary, "fixture.dylib");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new("macos-arm64-darwin")));
            byte[] fat = new byte[4128];
            BinaryPrimitives.WriteUInt32BigEndian(fat, wide ? 0xcafebabfu : 0xcafebabeu);
            BinaryPrimitives.WriteUInt32BigEndian(fat.AsSpan(4), 2);
            int entrySize = wide ? 32 : 20;
            BinaryPrimitives.WriteUInt32BigEndian(fat.AsSpan(8), 0x01000007);
            int second = 8 + entrySize;
            BinaryPrimitives.WriteUInt32BigEndian(fat.AsSpan(second), 0x0100000c);
            if (wide)
            {
                BinaryPrimitives.WriteUInt64BigEndian(fat.AsSpan(second + 8), 4096);
                BinaryPrimitives.WriteUInt64BigEndian(fat.AsSpan(second + 16), 32);
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(fat.AsSpan(second + 8), 4096);
                BinaryPrimitives.WriteUInt32BigEndian(fat.AsSpan(second + 12), 32);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(fat.AsSpan(4096), 0xfeedfacf);
            BinaryPrimitives.WriteUInt32LittleEndian(fat.AsSpan(4100), 0x0100000c);
            BinaryPrimitives.WriteUInt32LittleEndian(fat.AsSpan(4108), 6);
            File.WriteAllBytes(path, fat);
            NativeArtifactVerifier.Verify(path, target, NativeOutputKind.SharedLibrary);
            BinaryPrimitives.WriteUInt32BigEndian(fat.AsSpan(4096), 0xfeedfacf);
            BinaryPrimitives.WriteUInt32BigEndian(fat.AsSpan(4100), 0x0100000c);
            BinaryPrimitives.WriteUInt32BigEndian(fat.AsSpan(4108), 6);
            File.WriteAllBytes(path, fat);
            Assert.Contains("byte order", Assert.Throws<InvalidDataException>(() => NativeArtifactVerifier.Verify(path, target, NativeOutputKind.SharedLibrary)).Message);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static byte[] Elf(bool bigEndian, bool relocatable)
    {
        byte[] data = new byte[64];
        "\u007fELF"u8.CopyTo(data);
        data[4] = 2; data[5] = bigEndian ? (byte)2 : (byte)1; data[6] = 1;
        if (bigEndian) { BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16), relocatable ? (ushort)1 : (ushort)3); BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(18), 183); }
        else { BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(16), relocatable ? (ushort)1 : (ushort)3); BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18), 183); }
        return data;
    }

    [Fact]
    public void ArchiveRejectsThinAndUnknownMemberArchitecture()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-archive-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string path = Path.Combine(temporary, "fixture.lib");
            File.WriteAllBytes(path, "!<thin>\n"u8.ToArray());
            Assert.Throws<InvalidDataException>(() => NativeArtifactVerifier.Verify(path, "windows-x64-msvc", NativeOutputKind.StaticLibrary));
            File.WriteAllBytes(path, Archive(Coff(0xaa64)));
            Assert.Throws<InvalidDataException>(() => NativeArtifactVerifier.Verify(path, "windows-x64-msvc", NativeOutputKind.StaticLibrary));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
    [Fact]
    public void WasmObjectsRequireLinkingMetadataAndActual32BitMemoryBeyondInitialHeader()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-wasm-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string path = Path.Combine(temporary, "fixture.o");
            File.WriteAllBytes(path, "\0asm\x01\0\0\0"u8.ToArray());
            Assert.Throws<InvalidDataException>(() => NativeArtifactVerifier.Verify(path, "emscripten-wasm32-emscripten", NativeOutputKind.Object));
            File.WriteAllBytes(path, WasmObject(memory64: true));
            Assert.Throws<InvalidDataException>(() => NativeArtifactVerifier.Verify(path, "emscripten-wasm32-emscripten", NativeOutputKind.Object));
            File.WriteAllBytes(path, WasmObject(padding: 8192));
            NativeArtifactVerifier.Verify(path, "emscripten-wasm32-emscripten", NativeOutputKind.Object);
            File.WriteAllBytes(path, Archive(WasmObject(padding: 8192)));
            NativeArtifactVerifier.Verify(path, "emscripten-wasm32-emscripten", NativeOutputKind.StaticLibrary);
            File.WriteAllBytes(path, Archive(WasmObject(memory64: true, padding: 8192)));
            Assert.Throws<InvalidDataException>(() => NativeArtifactVerifier.Verify(path, "emscripten-wasm32-emscripten", NativeOutputKind.StaticLibrary));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
    [Fact]
    [Trait("Category", "WindowsLlvmWasmObject")]
    public async Task RealLlvmWasm32ObjectIsAcceptedAndWasm64ObjectIsRejected()
    {
        if (!OperatingSystem.IsWindows()) return;
        string compiler = NativeBuildToolDiscovery.FindExecutable("clang", "C:/Program Files/LLVM/bin/clang.exe")
            ?? throw new InvalidOperationException("The Windows LLVM object-format gate requires Clang.");
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-llvm-wasm-object-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string source = Path.Combine(temporary, "fixture.c"), object32 = Path.Combine(temporary, "fixture32.o"), object64 = Path.Combine(temporary, "fixture64.o");
            await File.WriteAllTextAsync(source, "int fixture_roundtrip(int value) { return value + 1; }\n");
            foreach ((string target, string output) in new[] { ("wasm32-unknown-unknown", object32), ("wasm64-unknown-unknown", object64) })
            {
                var result = await ProcessExecutor.ExecuteAsync(compiler, ["--target=" + target, "-std=c17", "-c", source, "-o", output], temporary, TimeSpan.FromMinutes(1));
                Assert.False(result.timedOut);
                Assert.True(result.exitCode == 0, result.standardError);
            }
            NativeArtifactVerifier.Verify(object32, "emscripten-wasm32-emscripten", NativeOutputKind.Object);
            Assert.Throws<InvalidDataException>(() => NativeArtifactVerifier.Verify(object64, "emscripten-wasm32-emscripten", NativeOutputKind.Object));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
    private static byte[] WasmObject(bool memory64 = false, int padding = 0)
    {
        using MemoryStream stream = new();
        stream.Write("\0asm\x01\0\0\0"u8);
        if (padding > 0)
        {
            using MemoryStream custom = new();
            custom.WriteByte(4); custom.Write("name"u8); custom.Write(new byte[padding]);
            Section(0, custom.ToArray());
        }
        Section(2, [1, 3, (byte)'e', (byte)'n', (byte)'v', 15, .. "__linear_memory"u8, 2, memory64 ? (byte)4 : (byte)0, 0]);
        Section(0, [7, .. "linking"u8, 2]);
        return stream.ToArray();
        void Section(byte kind, byte[] data)
        {
            stream.WriteByte(kind);
            uint length = (uint)data.Length;
            do { byte octet = (byte)(length & 0x7f); length >>= 7; if (length != 0) octet |= 0x80; stream.WriteByte(octet); } while (length != 0);
            stream.Write(data);
        }
    }
    private static byte[] Coff(ushort machine)
    {
        byte[] bytes = new byte[20];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, machine);
        return bytes;
    }
    private static byte[] Archive(byte[] member)
    {
        string header = "fixture.o/".PadRight(16) + "0".PadRight(12) + "0".PadRight(6) + "0".PadRight(6) + "644".PadRight(8)
            + member.Length.ToString(System.Globalization.CultureInfo.InvariantCulture).PadRight(10) + "`\n";
        using MemoryStream bytes = new();
        bytes.Write("!<arch>\n"u8);
        bytes.Write(Encoding.ASCII.GetBytes(header));
        bytes.Write(member);
        if ((member.Length & 1) != 0) bytes.WriteByte((byte)'\n');
        return bytes.ToArray();
    }
    private sealed class CaptureSink : IArtifactSink
    {
        internal Dictionary<string, byte[]> files { get; } = new(StringComparer.Ordinal);
        public string WriteText(string relativePath, string content) { files.Add(relativePath, Encoding.UTF8.GetBytes(content)); return relativePath; }
        public string WriteBytes(string relativePath, ReadOnlyMemory<byte> content) { files.Add(relativePath, content.ToArray()); return relativePath; }
    }
}
