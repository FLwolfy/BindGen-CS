using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Requests;
using BGCS.NativeBuild.Packaging;
using BGCS.Platforms.Assets;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeAssetPackageTests
{
    [Theory]
    [InlineData("windows-x64-msvc", "win-x64")]
    [InlineData("windows-arm64-msvc", "win-arm64")]
    [InlineData("linux-x64-gnu", "linux-x64")]
    [InlineData("linux-arm64-gnu", "linux-arm64")]
    [InlineData("linux-x64-musl", "linux-musl-x64")]
    [InlineData("linux-arm64-musl", "linux-musl-arm64")]
    [InlineData("macos-x64-darwin", "osx-x64")]
    [InlineData("macos-arm64-darwin", "osx-arm64")]
    public void AssetRidsRetainExactDesktopAbiIdentity(string target, string expected)
        => Assert.Equal(expected, RuntimeIdentifierMapper.Map(new BGCS.Contracts.Targets.NativeTargetId(target)));

    [Fact]
    public async Task CompleteMultiTargetPackageHasDeterministicPathsAndExactContentDigests()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs neutral native package " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var windows = await InputAsync("windows-x64-msvc", "sample.dll", Pe());
            var linux = await InputAsync("linux-x64-gnu", "libsample.so", Elf());
            var mac = await InputAsync("macos-arm64-darwin", "libsample.dylib", MachO());
            CaptureSink first = new(), second = new();
            NativeAssetPackageResult result = await new NativeAssetPackager().PackageAsync(new([windows, mac, linux], first), CancellationToken.None);
            await new NativeAssetPackager().PackageAsync(new([linux, windows, mac], second), CancellationToken.None);
            Assert.Equal(new[] { "linux-x64", "osx-arm64", "win-x64" }, result.assets.Select(asset => asset.runtimeIdentifier));
            Assert.All(result.assets, asset =>
            {
                Assert.Null(asset.buildItem);
                Assert.Equal("runtimes/" + asset.runtimeIdentifier + "/native/" + Path.GetFileName(asset.relativePath), asset.relativePath);
                Assert.Equal(asset.digest, Digest(first.files[asset.relativePath]));
            });
            Assert.Equal(first.files.Keys.Order(StringComparer.Ordinal), second.files.Keys.Order(StringComparer.Ordinal));
            foreach (string path in first.files.Keys) Assert.Equal(first.files[path], second.files[path]);
            using JsonDocument manifest = JsonDocument.Parse(first.files["bgcs.native-assets.json"]);
            Assert.Equal(3, manifest.RootElement.GetProperty("assets").GetArrayLength());
            Assert.Equal(5, first.files.Count);

            async Task<NativeAssetInput> InputAsync(string target, string name, byte[] data)
            {
                string path = Path.Combine(root, name);
                await File.WriteAllBytesAsync(path, data);
                return new(path, new(target), NativeOutputKind.SharedLibrary, Digest(data));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("architecture")]
    [InlineData("digest")]
    public async Task InvalidBatchMemberCannotEmitAPartialPackage(string mismatch)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs invalid native package " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] bytes = Elf();
            string valid = Path.Combine(root, "alibvalid.so"), invalid = Path.Combine(root, mismatch == "architecture" ? "zinvalid.dll" : "zlibinvalid.so");
            await File.WriteAllBytesAsync(valid, bytes);
            byte[] invalidBytes = mismatch == "architecture" ? Pe() : bytes;
            await File.WriteAllBytesAsync(invalid, invalidBytes);
            NativeAssetInput first = new(valid, new("linux-x64-gnu"), NativeOutputKind.SharedLibrary, Digest(bytes));
            NativeAssetInput second = new(invalid, new(mismatch == "architecture" ? "windows-arm64-msvc" : "linux-x64-gnu"), NativeOutputKind.SharedLibrary,
                mismatch == "digest" ? new(new string('0', 64)) : Digest(invalidBytes));
            CaptureSink sink = new();
            if (mismatch == "architecture")
                await Assert.ThrowsAsync<InvalidDataException>(async () => await new NativeAssetPackager().PackageAsync(new([first, second], sink), CancellationToken.None));
            else
                await Assert.ThrowsAsync<IOException>(async () => await new NativeAssetPackager().PackageAsync(new([first, second], sink), CancellationToken.None));
            Assert.Empty(sink.files);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ContentDigest Digest(byte[] bytes) => new(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    private static byte[] Elf()
    {
        byte[] bytes = new byte[64];
        "\u007fELF"u8.CopyTo(bytes); bytes[4] = 2; bytes[5] = 1; bytes[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18), 62);
        return bytes;
    }
    private static byte[] MachO()
    {
        byte[] bytes = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0xfeedfacf);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x0100000c);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 6);
        return bytes;
    }
    private static byte[] Pe()
    {
        byte[] bytes = new byte[128];
        "MZ"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3c), 0x40);
        "PE\0\0"u8.CopyTo(bytes.AsSpan(0x40));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x44), 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x56), 0x2000);
        return bytes;
    }
    private sealed class CaptureSink : IArtifactSink
    {
        internal Dictionary<string, byte[]> files { get; } = new(StringComparer.Ordinal);
        public string WriteText(string relativePath, string content) { files.Add(relativePath, Encoding.UTF8.GetBytes(content)); return relativePath; }
        public string WriteBytes(string relativePath, ReadOnlyMemory<byte> content) { files.Add(relativePath, content.ToArray()); return relativePath; }
    }
}
