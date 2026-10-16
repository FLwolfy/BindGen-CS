using System;
using System.IO;
using System.Linq;
using System.Threading;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.NativeBuild.Planning;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeLinkerPinTests
{
    [Fact]
    public void ClangPinsExactLinkerInBothDependencyProbeAndFinalLink()
    {
        using Fixture fixture = new();
        CompilerBuildProvider provider = new("clang", fixture.compiler, linker: fixture.linker);
        NativeBuildPlan plan = provider.Plan(fixture.request, CancellationToken.None);
        NativeBuildCommand link = Assert.Single(plan.commands, command => command.id == "link");
        Assert.Equal("--ld-path=" + fixture.linker, Assert.Single(link.arguments, argument => argument.StartsWith("--ld-path=", StringComparison.Ordinal)));
        Assert.Contains("--ld-path=" + fixture.linker, Assert.Single(plan.linkDependencyScans).command.arguments);
        Assert.Contains(fixture.linker, plan.externalInputFiles);
        Assert.Equal(fixture.linker, Assert.Single(((INativeBuildPrerequisiteProvider)provider).DescribePrerequisites(), tool => tool.role == "linker").path);
        Assert.False(File.Exists(plan.outputFile));
    }

    [Theory]
    [InlineData("--ld-path=another-linker", false)]
    [InlineData("-fuse-ld=another-linker", false)]
    [InlineData("--ld-path another-linker", true)]
    [InlineData("-fuse-ld another-linker", true)]
    public void ResponseFilesCannotReplaceThePinnedLinker(string selector, bool linkerArguments)
    {
        using Fixture fixture = new();
        string response = Path.Combine(fixture.root, "override.rsp");
        File.WriteAllText(response, selector + "\n");
        NativeBuildRequest request = linkerArguments
            ? fixture.request with { linkerArguments = ["@" + response] }
            : fixture.request with { compilerArguments = ["@" + response] };
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new CompilerBuildProvider("clang", fixture.compiler).Plan(request, CancellationToken.None));
        Assert.Contains("pinned linker", failure.Message);
        Assert.False(File.Exists(request.outputFile));
    }

    [Fact]
    public void ConfigurationLinkerTailCannotReplaceThePinnedLinker()
    {
        using Fixture fixture = new();
        string configuration = Path.Combine(fixture.root, "link options.cfg");
        File.WriteAllText(configuration, "$--ld-path=another-linker\n");
        NativeBuildRequest request = fixture.request with { compilerArguments = ["--config=" + configuration] };
        Assert.Throws<InvalidOperationException>(() => new CompilerBuildProvider("clang", fixture.compiler).Plan(request, CancellationToken.None));
        Assert.False(File.Exists(request.outputFile));
    }

    [Fact]
    public void GnuRejectsAnUnenforcedExplicitLinkerInsteadOfReportingItReady()
    {
        using Fixture fixture = new();
        Assert.Throws<NotSupportedException>(() => new CompilerBuildProvider("gnu", fixture.compiler, linker: fixture.linker));
        Assert.Throws<NotSupportedException>(() => new CompilerBuildProvider("gnu", fixture.compiler).Plan(fixture.request, CancellationToken.None));
        Assert.False(File.Exists(fixture.request.outputFile));
    }

    [Fact]
    public void MissingPinnedLinkerCannotFallBackToDriverSearch()
    {
        using Fixture fixture = new();
        File.Delete(fixture.linker);
        Assert.Throws<FileNotFoundException>(() => new CompilerBuildProvider("clang", fixture.compiler).Plan(fixture.request, CancellationToken.None));
        Assert.False(File.Exists(fixture.request.outputFile));
    }

    private sealed class Fixture : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs pinned linker 目录 & " + Guid.NewGuid().ToString("N"));
        internal string compiler { get; }
        internal string linker { get; }
        internal NativeBuildRequest request { get; }
        internal Fixture()
        {
            Directory.CreateDirectory(root);
            compiler = Path.Combine(root, "never-run-clang");
            linker = Path.Combine(root, "exact selected linker");
            string source = Path.Combine(root, "source.c");
            File.WriteAllText(compiler, "No native process is allowed during planning.");
            File.WriteAllText(linker, "The immutable external input must name this exact linker.");
            File.WriteAllText(source, "int native_roundtrip(int value) { return value; }\n");
            NativeTargetDescriptor target = new(new("opaque-linux-linker-fixture"), "linux", "x64", "gnu", "x86_64-unknown-linux-gnu",
                new(compilerPath: compiler, linkerPath: linker));
            request = new(target, [new(source, SourceLanguage.C, "c17")], root, Path.Combine(root, "never-created.so"));
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
