using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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

            Assert.Equal("linux-x64", linuxResult.runtimeIdentifier);
            Assert.Equal("osx-arm64", macResult.runtimeIdentifier);
            Assert.True(File.Exists(Path.Combine(package, "runtimes", "linux-x64", "native", "libsample.so")));
            Assert.True(File.Exists(Path.Combine(package, "runtimes", "osx-arm64", "native", "libsample.dylib")));
            using JsonDocument index = JsonDocument.Parse(File.ReadAllText(Path.Combine(package,
                NativeAssetLayout.C_MANIFESTFILENAME)));
            JsonElement assets = index.RootElement.GetProperty("assets");
            Assert.Equal(2, assets.GetArrayLength());
            Assert.Equal("linux-x64", assets[0].GetProperty("runtimeIdentifier").GetString());
            Assert.Equal("osx-arm64", assets[1].GetProperty("runtimeIdentifier").GetString());
            Assert.All(assets.EnumerateArray(), asset => Assert.Equal(64,
                asset.GetProperty("sha256").GetString()!.Length));
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

            Assert.Equal("win-x64", result.runtimeIdentifier);
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

    [Fact]
    public void Stage_InvalidExistingIndexPreservesInstalledBinary()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-invalid-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string source = Path.Combine(temp, "libsample.so");
            string package = Path.Combine(temp, "package");
            byte[] original = Elf(62);
            File.WriteAllBytes(source, original);
            NativeAssetLayoutResult installed = NativeAssetLayout.Stage(CreateManifest("linux-x64-gnu"), source, package);
            File.WriteAllText(installed.manifestPath, "{\"assets\":null}");
            byte[] changed = Elf(62);
            changed[32] = 99;
            File.WriteAllBytes(source, changed);

            Assert.Throws<InvalidDataException>(() => NativeAssetLayout.Stage(CreateManifest("linux-x64-gnu"), source, package));

            Assert.Equal(original, File.ReadAllBytes(installed.assetPath));
            Assert.Equal("{\"assets\":null}", File.ReadAllText(installed.manifestPath));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public async Task Stage_ConcurrentPublishersRetainAllAssetsAndPackageContents()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-concurrent-package-" + Guid.NewGuid().ToString("N"));
        string package = Path.Combine(temp, "package");
        Directory.CreateDirectory(package);
        try
        {
            File.WriteAllText(Path.Combine(package, "NOTICE.txt"), "Package notice");
            string[] binaries = Enumerable.Range(0, 4).Select(index => Path.Combine(temp, $"libsample{index}.so")).ToArray();
            foreach (string binary in binaries)
                File.WriteAllBytes(binary, Elf(62));

            await Task.WhenAll(binaries.Select((
                binary,
                index
            ) => Task.Run(() => NativeAssetLayout.Stage(
                CreateManifest("linux-x64-gnu") with { libraryName = $"sample{index}" }, binary, package))));

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(package, NativeAssetLayout.C_MANIFESTFILENAME)));
            Assert.Equal(4, document.RootElement.GetProperty("assets").GetArrayLength());
            Assert.Equal("Package notice", File.ReadAllText(Path.Combine(package, "NOTICE.txt")));
            Assert.Equal(4, Directory.GetFiles(package, "*.so", SearchOption.AllDirectories).Length);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
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
        target, null, null, null, "c++23", "sample", [], [], [], [], [], [], [], [], [], []);
}
