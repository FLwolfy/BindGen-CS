using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Requests;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeModelSnapshotTests
{
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
