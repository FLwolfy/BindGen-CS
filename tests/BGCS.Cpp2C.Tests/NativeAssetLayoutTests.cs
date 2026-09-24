using System;
using System.IO;
using System.Text.Json;
using BGCS.Cpp2C.Build;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class NativeAssetLayoutTests
{
    [Theory]
    [InlineData("windows-x64-msvc", "win-x64")]
    [InlineData("windows-arm64-msvc", "win-arm64")]
    [InlineData("linux-x64-gnu", "linux-x64")]
    [InlineData("linux-arm64-gnu", "linux-arm64")]
    [InlineData("macos-x64-darwin", "osx-x64")]
    [InlineData("macos-arm64-darwin", "osx-arm64")]
    public void GetRuntimeIdentifier_MapsSupportedDesktopTargets(string target, string expected) =>
        Assert.Equal(expected, NativeAssetLayout.GetRuntimeIdentifier(target));

    [Theory]
    [InlineData("android-arm64-android")]
    [InlineData("ios-arm64-darwin")]
    [InlineData("freebsd-x64-gnu")]
    public void GetRuntimeIdentifier_DoesNotClaimDeferredTargets(string target) =>
        Assert.Throws<NotSupportedException>(() => NativeAssetLayout.GetRuntimeIdentifier(target));

    [Fact]
    public void Stage_MergesTargetsIntoDeterministicNuGetRuntimeLayout()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-native-layout-" + Guid.NewGuid().ToString("N"));
        string binaries = Path.Combine(temp, "bin");
        string package = Path.Combine(temp, "package");
        Directory.CreateDirectory(binaries);
        try
        {
            string linux = Path.Combine(binaries, "libsample.so");
            string macos = Path.Combine(binaries, "libsample.dylib");
            File.WriteAllBytes(linux, Elf(62));
            File.WriteAllBytes(macos, MachO(0x0c));

            NativeAssetLayoutResult linuxResult = NativeAssetLayout.Stage(
                CreateManifest("linux-x64-gnu"), linux, package);
            NativeAssetLayoutResult macResult = NativeAssetLayout.Stage(
                CreateManifest("macos-arm64-darwin"), macos, package);

            Assert.Equal("linux-x64", linuxResult.RuntimeIdentifier);
            Assert.Equal("osx-arm64", macResult.RuntimeIdentifier);
            Assert.True(File.Exists(Path.Combine(package, "runtimes", "linux-x64", "native", "libsample.so")));
            Assert.True(File.Exists(Path.Combine(package, "runtimes", "osx-arm64", "native", "libsample.dylib")));
            using JsonDocument index = JsonDocument.Parse(File.ReadAllText(Path.Combine(package,
                NativeAssetLayout.ManifestFileName)));
            JsonElement assets = index.RootElement.GetProperty("Assets");
            Assert.Equal(2, assets.GetArrayLength());
            Assert.Equal("linux-x64", assets[0].GetProperty("RuntimeIdentifier").GetString());
            Assert.Equal("osx-arm64", assets[1].GetProperty("RuntimeIdentifier").GetString());
            Assert.All(assets.EnumerateArray(), asset => Assert.Equal(64,
                asset.GetProperty("Sha256").GetString()!.Length));
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Stage_RejectsWrongBinaryArchitectureBeforeWritingPackage()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-wrong-rid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string library = Path.Combine(temp, "libsample.so");
            File.WriteAllBytes(library, Elf(62));

            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                NativeAssetLayout.Stage(CreateManifest("linux-arm64-gnu"), library, Path.Combine(temp, "package")));

            Assert.Contains("linux-arm64-gnu", error.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(temp, "package")));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Stage_AcceptsWindowsPeWithMatchingMachine()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-pe-rid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            byte[] pe = new byte[128];
            pe[0] = (byte)'M';
            pe[1] = (byte)'Z';
            pe[0x3c] = 0x40;
            pe[0x40] = (byte)'P';
            pe[0x41] = (byte)'E';
            pe[0x44] = 0x64;
            pe[0x45] = 0x86;
            string library = Path.Combine(temp, "sample.dll");
            File.WriteAllBytes(library, pe);

            NativeAssetLayoutResult result = NativeAssetLayout.Stage(
                CreateManifest("windows-x64-msvc"), library, Path.Combine(temp, "package"));

            Assert.Equal("win-x64", result.RuntimeIdentifier);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private static byte[] Elf(byte machine)
    {
        byte[] bytes = new byte[64];
        bytes[0] = 0x7f;
        bytes[1] = (byte)'E';
        bytes[2] = (byte)'L';
        bytes[3] = (byte)'F';
        bytes[4] = 2;
        bytes[5] = 1;
        bytes[18] = machine;
        return bytes;
    }

    private static byte[] MachO(byte cpu)
    {
        byte[] bytes = new byte[64];
        bytes[0] = 0xcf;
        bytes[1] = 0xfa;
        bytes[2] = 0xed;
        bytes[3] = 0xfe;
        bytes[4] = cpu;
        bytes[7] = 1;
        return bytes;
    }

    private static CppBridgeBuildManifest CreateManifest(string target) => new(
        1, target, null, null, null, "c++23", "sample", [], [], [], [], [], [], [], [], [], []);
}
