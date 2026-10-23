using BGCS.Core.Hosting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class ScopedFileSnapshotTests : IDisposable
{
    private readonly IHostPathService m_paths = HostTestPaths.Create();
    // These integration controls cover stable aliases and mutations between
    // independent verifier calls. PhysicalPathResolutionScopeTests exercises
    // deterministic retargeting between observation and final revalidation.
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-scoped-files-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> m_links = [];

    public ScopedFileSnapshotTests() => Directory.CreateDirectory(m_root);

    [Theory]
    [InlineData("small")]
    [InlineData("many")]
    [InlineData("large")]
    public async Task AliasAndSiblingDependenciesKeepFullByteChecksAcrossIndependentCalls(string mode)
    {
        var fixture = await Capture(mode);
        InputDependency direct = Assert.Single(fixture.snapshot.files, input => input.path == fixture.original);
        InputDependency alias = Assert.Single(fixture.snapshot.files, input => input.path == fixture.aliasFile);
        Assert.NotEqual(direct.path, alias.path);
        Assert.Equal(direct.resolvedPath, alias.resolvedPath);
        Assert.Equal(direct.digest, alias.digest);
        Assert.Null(SourceSnapshotVerifier.GetMismatch(fixture.snapshot, m_paths));

        DateTime timestamp = File.GetLastWriteTimeUtc(fixture.original);
        byte previous;
        using (FileStream file = new(fixture.original, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            file.Position = file.Length - 1;
            previous = checked((byte)file.ReadByte());
            file.Position--;
            file.WriteByte((byte)(previous ^ 0xff));
        }
        File.SetLastWriteTimeUtc(fixture.original, timestamp);
        InputDependency earliest = fixture.snapshot.files.First(input => input.resolvedPath == direct.resolvedPath);
        Assert.Equal("Input file bytes changed: " + earliest.path, SourceSnapshotVerifier.GetMismatch(fixture.snapshot, m_paths));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(fixture.original));

        using (FileStream file = new(fixture.original, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            file.Position = file.Length - 1;
            file.WriteByte(previous);
        }
        File.SetLastWriteTimeUtc(fixture.original, timestamp);
        Assert.Null(SourceSnapshotVerifier.GetMismatch(fixture.snapshot, m_paths));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContradictoryFileRecordsKeepIndexedMismatchPrecedence(bool manyFiles)
    {
        var fixture = await Capture(manyFiles ? "many" : "small");
        InputDependency original = fixture.snapshot.files[0];
        InputDependency wrongBytes = original with { digest = new ContentDigest(new string('0', 64)) };
        InputDependency wrongIdentity = original with { resolvedPath = original.resolvedPath + "-wrong" };
        ImmutableArray<InputDependency> inputs = ImmutableArray.Create(original, wrongBytes, wrongIdentity)
            .AddRange(fixture.snapshot.files.Skip(1));
        SourceSnapshot contradictory = fixture.snapshot with { files = inputs };
        Assert.Equal("Input file bytes changed: " + original.path, SourceSnapshotVerifier.GetMismatch(contradictory, m_paths));
        Assert.Null(SourceSnapshotVerifier.GetMismatch(fixture.snapshot, m_paths));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANewCallRechecksAnAncestorAliasEvenWhenReplacementBytesAreIdentical(bool manyFiles)
    {
        var fixture = await Capture(manyFiles ? "many" : "small");
        Assert.Null(SourceSnapshotVerifier.GetMismatch(fixture.snapshot, m_paths));
        string replacement = Directory.CreateDirectory(Path.Combine(m_root, "replacement")).FullName;
        File.Copy(fixture.original, Path.Combine(replacement, Path.GetFileName(fixture.original)));
        string alias = Path.GetDirectoryName(fixture.aliasFile)!;
        DeleteLink(alias);
        m_links.Remove(alias);
        await Link(alias, replacement);

        Assert.Equal("An input file disappeared or was redirected: " + fixture.aliasFile,
            SourceSnapshotVerifier.GetMismatch(fixture.snapshot, m_paths));
        SourceSnapshot current = new ExternalInputSnapshotBuilder().Capture(
            [new(fixture.aliasFile, ExternalInputKind.File)], m_root, m_paths);
        Assert.NotEqual(Assert.Single(current.files).resolvedPath,
            Assert.Single(fixture.snapshot.files, input => input.path == fixture.aliasFile).resolvedPath);
        Assert.Null(SourceSnapshotVerifier.GetMismatch(current, m_paths));
    }

    private async Task<(SourceSnapshot snapshot, string original, string aliasFile)> Capture(string mode)
    {
        string input = Directory.CreateDirectory(Path.Combine(m_root, "inputs")).FullName;
        string sibling = Directory.CreateDirectory(Path.Combine(m_root, "siblings", "nested")).FullName;
        ImmutableArray<DeclaredExternalInput>.Builder inputs = ImmutableArray.CreateBuilder<DeclaredExternalInput>();
        string original = Path.Combine(input, "original.bin");
        WriteBytes(original, mode == "large" ? 8388641 : 257, 1);
        inputs.Add(new(original, ExternalInputKind.File));
        for (int index = 1; index < (mode == "many" ? 64 : 3); index++)
        {
            string path = Path.Combine(index % 2 == 0 ? input : sibling,
                index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + ".bin");
            WriteBytes(path, 257, index);
            inputs.Add(new(path, ExternalInputKind.File));
        }
        string alias = Path.Combine(m_root, "alias");
        await Link(alias, input);
        string aliasFile = Path.Combine(alias, Path.GetFileName(original));
        inputs.Add(new(aliasFile, ExternalInputKind.File));
        SourceSnapshot snapshot = new ExternalInputSnapshotBuilder().Capture(inputs.ToImmutable(), m_root, m_paths);
        Assert.Equal(mode == "many" ? 65 : 4, snapshot.files.Length);
        return (snapshot, original, aliasFile);
    }

    private static void WriteBytes(string path, int length, int seed)
    {
        byte[] buffer = Enumerable.Range(0, Math.Min(length, 65536)).Select(index => (byte)(index * 31 + seed)).ToArray();
        using FileStream output = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        for (int remaining = length; remaining > 0;)
        {
            int count = Math.Min(remaining, buffer.Length);
            output.Write(buffer, 0, count);
            remaining -= count;
        }
    }

    private async Task Link(string path, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            string command = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + path.Replace("'", "''", StringComparison.Ordinal)
                + "' -Target '" + target.Replace("'", "''", StringComparison.Ordinal) + "' | Out-Null";
            var result = await ProcessExecutor.ExecuteAsync(executable,
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command))], m_root, TimeSpan.FromSeconds(30));
            if (new DirectoryInfo(path).LinkTarget is not null) m_links.Add(path);
            Assert.True(result.exitCode == 0, result.standardOutput + Environment.NewLine + result.standardError);
        }
        else { Directory.CreateSymbolicLink(path, target); m_links.Add(path); }
    }

    private static void DeleteLink(string path) { if (OperatingSystem.IsWindows()) Directory.Delete(path); else File.Delete(path); }

    public void Dispose()
    {
        for (int index = m_links.Count - 1; index >= 0; index--) DeleteLink(m_links[index]);
        Directory.Delete(m_root, recursive: true);
    }
}
