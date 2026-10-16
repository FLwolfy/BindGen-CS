using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Requests;
using BGCS.Platforms.Targeting;
using BGCS.NativeBuild.Execution;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeModelSnapshotTests
{
    [Theory]
    [InlineData("")]
    [InlineData("bgcs-invalid-output-encoding")]
    public async Task UnknownDeclaredOutputEncodingRejectsThePlanBeforeWritingInputsOrLaunchingTools(string encoding)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "bgcs-invalid-native-encoding-" + Guid.NewGuid().ToString("N"));
        NativeBuildCommand command = new("must-not-launch", Path.Combine(scratch, "not-a-tool"), [], scratch) { outputEncoding = encoding };
        NativeBuildPlan plan = new("probe", new TargetResolver().Resolve(new(new("host"))), [command], Path.Combine(scratch, "output.obj"))
        { scratchDirectory = scratch, generatedInputFiles = [new("must-not-write.txt", "never publish an invalid plan")] };
        await Assert.ThrowsAsync<ArgumentException>(() => new PlannedNativeBuildExecutor().ExecuteAsync(plan, TimeSpan.FromSeconds(10)));
        Assert.False(Directory.Exists(scratch));
    }

    [Fact]
    public void NativePlanClosureAndExportInspectionDoNotRetainMutableInputLists()
    {
        List<string> arguments = ["-c", "source.cpp"];
        NativeBuildCommand command = new("compile", "compiler", arguments.ToImmutableArray(), ".");
        List<NativeBuildCommand> commands = [command];
        NativeBuildPlan plan = new("compiler", new TargetResolver().Resolve(new(new("windows-x64-msvc"))), commands.ToImmutableArray(), "output.obj");
        List<string> expected = ["required"], actual = ["required"];
        NativeExportInspectionResult result = new("inspector", expected, actual, []);
        arguments.Clear(); commands.Clear(); expected.Clear(); actual.Clear();
        Assert.Equal(new[] { "-c", "source.cpp" }, Assert.Single(plan.commands).arguments);
        Assert.Equal("required", Assert.Single(result.expected));
        Assert.Equal("required", Assert.Single(result.actual));
        Assert.True(result.success);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.expected).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.actual)[0] = "changed");
    }
}
