using System;
using System.IO;
using System.Linq;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class NativeArgumentExpansionTests
{
    [Fact]
    public void ConfigOptionsPrecedeCommandLineAndDollarOptionsRemainLinkerOnly()
    {
        string temporary = Temporary();
        try
        {
            string config = Path.Combine(temporary, "preset.cfg");
            File.WriteAllText(config, "-DNATIVE_VALUE=9\n$-Wl,-Bstatic $-lmath\n");
            NativeArgumentExpansionResult result = new NativeArgumentInputSnapshotBuilder().Expand(["-DNATIVE_VALUE=7", "--config=" + config], temporary);
            Assert.Equal(new[] { "-DNATIVE_VALUE=9", "-DNATIVE_VALUE=7" }, result.arguments.ToArray());
            Assert.Equal(new[] { "-Xlinker", "-Bstatic", "-lmath" }, result.linkerOnlyArguments.ToArray());
            Assert.Equal(config, Assert.Single(result.snapshot.files).path);
            Assert.True(SourceSnapshotVerifier.Matches(result.snapshot));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void HiddenTargetAndParserResourceSubstitutionsAreRejectedAfterExpansion()
    {
        string temporary = Temporary();
        try
        {
            string config = Path.Combine(temporary, "hidden.cfg"), response = Path.Combine(temporary, "hidden.rsp");
            File.WriteAllText(config, "@hidden.rsp\n");
            File.WriteAllText(response, "-Xclang -target-abi -Xclang unsupported-abi\n");
            NativeArgumentExpansionResult result = new NativeArgumentInputSnapshotBuilder().Expand(["--config=" + config], temporary);
            Assert.Equal(2, result.snapshot.files.Length);
            Assert.Contains("resolved CPU", Assert.Throws<ArgumentException>(() => NativeArgumentIdentityValidator.Validate(result.arguments, "test frontend", true)).Message);
            Assert.Throws<ArgumentException>(() => NativeArgumentIdentityValidator.Validate(["/clang:--target=x86_64-linux-musl"], "test native"));
            Assert.Throws<NotSupportedException>(() => NativeArgumentIdentityValidator.Validate(["-Xclang", "-load", "-Xclang", "plugin.dll"], "test frontend", true));
            Assert.Throws<NotSupportedException>(() => NativeArgumentIdentityValidator.Validate(["-specs=hidden.specs"], "test native"));
            Assert.Throws<NotSupportedException>(() => NativeArgumentIdentityValidator.Validate(["-fplugin=mutate-abi.so"], "test native"));
            Assert.Throws<NotSupportedException>(() => NativeArgumentIdentityValidator.Validate(["/clang:-Xclang", "/clang:-load", "/clang:mutate-abi.dll"], "test native"));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void RawDriverFileAndSearchInputsRemainLockedWhileOnlyOwnedMembershipIsExcluded()
    {
        string temporary = Temporary();
        try
        {
            string includes = Path.Combine(temporary, "includes"), owned = Path.Combine(includes, "owned"), header = Path.Combine(includes, "header.h"), overlay = Path.Combine(temporary, "overlay.yaml");
            Directory.CreateDirectory(owned);
            File.WriteAllText(header, "#define VALUE 7\n");
            File.WriteAllText(overlay, "version: 0\n");
            NativeArgumentExpansionResult result = new NativeArgumentInputSnapshotBuilder().Expand(["-I", includes, "-include", header, "-Xclang", "-ivfsoverlay", "-Xclang", overlay], temporary,
                snapshotExcludedDirectories: [owned]);
            Assert.Equal(2, result.snapshot.files.Length);
            Assert.Single(result.snapshot.searchDirectories);
            Assert.Empty(result.cacheIneligibilityReasons);
            File.WriteAllText(Path.Combine(owned, "produced.obj"), "owned output");
            Assert.True(SourceSnapshotVerifier.Matches(result.snapshot));
            File.AppendAllText(overlay, "changed: true\n");
            Assert.False(SourceSnapshotVerifier.Matches(result.snapshot));
            NativeArgumentExpansionResult recaptured = new NativeArgumentInputSnapshotBuilder().Expand(["-I", includes], temporary, snapshotExcludedDirectories: [owned]);
            File.WriteAllText(Path.Combine(includes, "new.h"), "new higher priority candidate");
            Assert.False(SourceSnapshotVerifier.Matches(recaptured.snapshot));
            Assert.NotEmpty(new NativeArgumentInputSnapshotBuilder().Expand(["-fmodules"], temporary).cacheIneligibilityReasons);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static string Temporary()
    {
        string path = Path.Combine(Path.GetTempPath(), "bgcs-argument-expansion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
