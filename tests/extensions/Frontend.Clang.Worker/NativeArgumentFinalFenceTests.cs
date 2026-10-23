using BGCS.Frontend.Clang;
using BGCS.Frontend.Clang.Workers;
using BGCS.Frontend.Clang.Parsing.Arguments;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BGCS.Core.Inputs;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Worker.Tests;

public sealed class NativeArgumentFinalFenceTests : WorkerTestHost
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-argument-fence-" + Guid.NewGuid().ToString("N"));

    public NativeArgumentFinalFenceTests() => Directory.CreateDirectory(m_root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalSupersetFenceRejectsResponseMutationDuringClassification(bool nestedConfig)
    {
        string response = Path.Combine(m_root, "arguments.rsp"), config = Path.Combine(m_root, "preset.cfg"), owned = Path.Combine(m_root, "owned");
        Write(response, "-DVALUE=7");
        Write(config, "@arguments.rsp");
        Directory.CreateDirectory(owned);
        DateTime timestamp = File.GetLastWriteTimeUtc(response);
        int observations = 0;
        IEnumerable<string> MutatingExclusions()
        {
            // Responses/configs have already been expanded when the classifier
            // enumerates these public input declarations. No private fault hook
            // or timing race is needed to change an already observed input.
            observations++;
            Write(response, "-DVALUE=9");
            File.SetLastWriteTimeUtc(response, timestamp);
            yield return owned;
        }

        IOException failure = Assert.Throws<IOException>(() => new NativeArgumentInputSnapshotBuilder(host.paths).Expand(
            nestedConfig ? ["--config=" + config] : ["@" + response], m_root,
            snapshotExcludedDirectories: MutatingExclusions()));

        Assert.Equal(1, observations);
        Assert.Contains("Native argument inputs changed", failure.Message, StringComparison.Ordinal);
        Assert.Equal("-DVALUE=9", File.ReadAllText(response));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(response));
    }

    [Fact]
    public void MissingHigherPriorityConfigAndChosenHeaderRemainInTheFinalClosure()
    {
        string user = Path.Combine(m_root, "user"), system = Path.Combine(m_root, "system"), includes = Path.Combine(system, "include");
        Directory.CreateDirectory(user);
        Directory.CreateDirectory(includes);
        string selected = Path.Combine(system, "preset.cfg"), header = Path.Combine(includes, "locked.h"), preferred = Path.Combine(user, "preset.cfg");
        Write(selected, "-I <CFGDIR>/include -include locked.h");
        Write(header, "#define VALUE 7\n");
        NativeArgumentExpansionResult result = new NativeArgumentInputSnapshotBuilder(host.paths).Expand(
            ["--config-user-dir=" + user, "--config-system-dir=" + system, "--config", "preset.cfg"], m_root,
            compilerPath: Path.Combine(m_root, "compiler", "clang"));

        Assert.Equal(new[] { selected, header }.Order(StringComparer.Ordinal), result.snapshot.files.Select(input => input.path).Order(StringComparer.Ordinal));
        Assert.Contains(result.snapshot.absentInputs, input => input.path == preferred);
        Assert.Contains(result.snapshot.absentInputs, input => input.path == Path.Combine(m_root, "locked.h"));
        Assert.Equal(includes, Assert.Single(result.snapshot.searchDirectories).path);
        Assert.True(SourceSnapshotVerifier.Matches(result.snapshot, host.paths));

        Write(preferred, "-DVALUE=9");
        Assert.Contains(preferred, SourceSnapshotVerifier.GetMismatch(result.snapshot, host.paths)!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-I")]
    [InlineData("-isystem")]
    [InlineData("-iframework")]
    [InlineData("--sysroot")]
    public void CompleteSearchMembershipAndInputBytesSurviveTheSingleFinalFence(string option)
    {
        string includes = Path.Combine(m_root, "sdk"), owned = Path.Combine(includes, "owned"), header = Path.Combine(includes, "locked.h");
        Directory.CreateDirectory(owned);
        Write(header, "#define VALUE 7\n");
        NativeArgumentExpansionResult result = new NativeArgumentInputSnapshotBuilder(host.paths).Expand(
            [option, includes, "-include", "locked.h"], m_root,
            snapshotExcludedDirectories: [owned], includeSearchDirectories: [includes]);
        Assert.Equal(header, Assert.Single(result.snapshot.files).path);
        Assert.Equal(includes, Assert.Single(result.snapshot.searchDirectories).path);
        Assert.Equal(owned, Assert.Single(result.snapshot.excludedDirectories));
        Assert.True(SourceSnapshotVerifier.Matches(result.snapshot, host.paths));
        Write(Path.Combine(owned, "generated.obj"), "private output");
        Assert.True(SourceSnapshotVerifier.Matches(result.snapshot, host.paths));

        DateTime directoryTimestamp = Directory.GetLastWriteTimeUtc(includes);
        string added = Path.Combine(includes, "new-candidate.any");
        Write(added, "candidate");
        Directory.SetLastWriteTimeUtc(includes, directoryTimestamp);
        Assert.Contains("membership changed", SourceSnapshotVerifier.GetMismatch(result.snapshot, host.paths)!, StringComparison.Ordinal);
        File.Delete(added);
        Assert.True(SourceSnapshotVerifier.Matches(result.snapshot, host.paths));

        DateTime timestamp = File.GetLastWriteTimeUtc(header);
        Write(header, "#define VALUE 9\n");
        File.SetLastWriteTimeUtc(header, timestamp);
        Assert.Contains("Input file bytes changed", SourceSnapshotVerifier.GetMismatch(result.snapshot, host.paths)!, StringComparison.Ordinal);
    }

    [Fact]
    public void OneFileUsedAsResponseAndExplicitInputRetainsItsExactByteProof()
    {
        string response = Path.Combine(m_root, "shared.rsp");
        Write(response, "-DVALUE=7");
        NativeArgumentExpansionResult result = new NativeArgumentInputSnapshotBuilder(host.paths).Expand(["@" + response, "-include", response], m_root);

        Assert.Equal(new[] { "-DVALUE=7", "-include", response }, result.arguments.ToArray());
        InputDependency input = Assert.Single(result.snapshot.files);
        Assert.Equal(response, input.path);
        Assert.True(SourceSnapshotVerifier.Matches(result.snapshot, host.paths));
        DateTime timestamp = File.GetLastWriteTimeUtc(response);
        Write(response, "-DVALUE=9");
        File.SetLastWriteTimeUtc(response, timestamp);
        Assert.Contains(response, SourceSnapshotVerifier.GetMismatch(result.snapshot, host.paths)!, StringComparison.Ordinal);
    }

    private static void Write(
        string path,
        string text
    ) => File.WriteAllText(path, text, new UTF8Encoding(false));

    public override void Dispose() => Directory.Delete(m_root, recursive: true);
}
