using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;
using BGCS.Contracts.Requests;
using BGCS.NativeBuild.Validation;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeArtifactKindTests
{
    [Theory]
    [InlineData("windows-x64-msvc")]
    [InlineData("linux-x64-gnu")]
    [InlineData("macos-x64-darwin")]
    public void SharedArtifactsMustDeclareTheirActualLibraryKind(string targetId)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs native artifact kind " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "fixture");
            byte[] data = SharedHeader(targetId);
            File.WriteAllBytes(path, data);
            var target = new TargetResolver().Resolve(new(new(targetId)));
            NativeArtifactVerifier.Verify(path, target, NativeOutputKind.SharedLibrary);
            if (target.platformId == "windows") data[0x57] = 0;
            else if (target.platformId == "linux") BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(16), 1);
            else BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 1);
            File.WriteAllBytes(path, data);
            Assert.Throws<InvalidDataException>(() => NativeArtifactVerifier.Verify(path, target, NativeOutputKind.SharedLibrary));
            if (target.platformId != "windows") NativeArtifactVerifier.Verify(path, target, NativeOutputKind.Object);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("windows-x64-msvc")]
    [InlineData("linux-x64-gnu")]
    [InlineData("macos-x64-darwin")]
    public async Task MissingExplicitExportToolCannotUseAnInstalledFallback(string targetId)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs export tool override " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "fixture"), missing = Path.Combine(root, "missing-explicit-inspector");
            File.WriteAllBytes(path, SharedHeader(targetId));
            var target = new TargetResolver().Resolve(new(new(targetId)));
            FileNotFoundException failure = await Assert.ThrowsAsync<FileNotFoundException>(() => NativeExportInspector.InspectAsync(target, path, ["required_export"], toolPath: missing));
            Assert.Equal(missing, failure.FileName);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static byte[] SharedHeader(string targetId)
    {
        byte[] data = new byte[128];
        if (targetId.StartsWith("windows-", StringComparison.Ordinal))
        {
            "MZ"u8.CopyTo(data);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3c), 0x40);
            "PE\0\0"u8.CopyTo(data.AsSpan(0x40));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x44), 0x8664);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x56), 0x2000);
        }
        else if (targetId.StartsWith("linux-", StringComparison.Ordinal))
        {
            "\u007fELF"u8.CopyTo(data);
            data[4] = 2; data[5] = 1; data[6] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(16), 3);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18), 62);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data, 0xfeedfacf);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0x01000007);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 6);
        }
        return data;
    }
}
