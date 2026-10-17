using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.NativeBuild.Validation;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class GnuWorkingDirectoryMetadataTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactCompilerWorkingDirectoryPreambleIsNotAnIncludeDependency(bool dotSegment)
    {
        using Fixture fixture = new();
        string directory = dotSegment ? Path.Combine(fixture.root, ".") : fixture.root;
        string raw = fixture.Trace(directory);
        await File.WriteAllTextAsync(fixture.operation.outputFile, raw);
        var adapted = await NativeEffectiveSourceAdapter.PrepareAsync(fixture.plan, fixture.operation);
        string actual = await File.ReadAllTextAsync(adapted.sourceFile);
        Assert.Contains("int selected();", actual);
        Assert.DoesNotContain(Quote(directory + "//"), actual);
        Assert.Null(adapted.declarationPrelude);
        Assert.Equal(raw, await File.ReadAllTextAsync(fixture.operation.outputFile));
    }

    [Theory]
    [InlineData("cwd")]
    [InlineData("source")]
    [InlineData("following")]
    [InlineData("flags")]
    [InlineData("first-line")]
    [InlineData("late")]
    [InlineData("user-metadata")]
    [InlineData("missing-context")]
    public async Task SimilarOrUserAuthoredDirectoryMarkersCannotBypassDependencyAuthentication(string invalid)
    {
        using Fixture fixture = new();
        string raw = invalid switch
        {
            "cwd" => fixture.Trace(Path.Combine(fixture.root, "different")),
            "source" => fixture.Trace(fixture.root).Replace("# 0 " + Quote(fixture.operation.source), "# 0 " + Quote(Path.Combine(fixture.root, "different.cpp")), StringComparison.Ordinal),
            "following" => fixture.Trace(fixture.root).Replace("# 0 \"<built-in>\"", "# 0 \"<command-line>\"", StringComparison.Ordinal),
            "flags" => fixture.Trace(fixture.root).Replace(Quote(fixture.root + "//") + "\n", Quote(fixture.root + "//") + " 1\n", StringComparison.Ordinal),
            "first-line" => fixture.Trace(fixture.root).Replace("# 0 " + Quote(fixture.operation.source), "# 1 " + Quote(fixture.operation.source), StringComparison.Ordinal),
            "late" => "# 0 " + Quote(fixture.operation.source) + "\n# 0 \"<built-in>\"\n# 1 " + Quote(fixture.root + "//") + "\nint selected();\n",
            "user-metadata" => fixture.Trace(fixture.root) + "# 1 " + Quote(fixture.root + "//") + "\nint later();\n",
            _ => fixture.Trace(fixture.root)
        };
        await File.WriteAllTextAsync(fixture.operation.outputFile, raw);
        NativePreprocessingOperation operation = invalid == "missing-context" ? fixture.operation with { command = fixture.operation.command with { workingDirectory = null! } } : fixture.operation;
        await Assert.ThrowsAsync<NotSupportedException>(async () => await NativeEffectiveSourceAdapter.PrepareAsync(fixture.plan, operation));
        Assert.Equal(raw, await File.ReadAllTextAsync(fixture.operation.outputFile));
    }

    private static string Quote(string text) => JsonSerializer.Serialize(text);

    private sealed class Fixture : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs gnu cwd metadata " + Guid.NewGuid().ToString("N"));
        internal NativeBuildPlan plan { get; }
        internal NativePreprocessingOperation operation { get; }

        internal Fixture()
        {
            Directory.CreateDirectory(root);
            NativeTargetDescriptor target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp, "clang");
            string source = Path.Combine(root, "source.cpp"), output = Path.Combine(root, "native.cpp"), dependencies = Path.Combine(root, "native.d");
            File.WriteAllText(source, "int selected();\n");
            File.WriteAllText(dependencies, "native.o: " + source.Replace('\\', '/').Replace(" ", "\\ ", StringComparison.Ordinal) + "\n");
            operation = new(new("preprocess", target.toolchain.compilerPath!, ["-g", "-E", source], root), source, SourceLanguage.Cpp, "c++23", output)
            { dependencyFile = new(dependencies, NativeDependencyFormat.Make, source) { workingDirectory = root } };
            // Only the boundary reader is supplied synthetic GCC metadata. Header
            // discovery still uses an actual installed compiler, with no GNU run
            // claimed on hosts that have only Clang.
            plan = new("gnu", target, [operation.command], Path.Combine(root, "unused.o"))
            { compilerFamily = "gnu", compilerPath = target.toolchain.compilerPath, preprocessingOperations = [operation], scratchDirectory = root };
        }

        // GCC specifies the SECOND marker as cwd + two slashes, also implied by
        // -g: https://gcc.gnu.org/onlinedocs/gcc-13.1.0/gcc/Preprocessor-Options.html
        internal string Trace(string directory) => "# 0 " + Quote(operation.source) + "\n# 1 " + Quote(directory + "//")
            + "\n# 0 \"<built-in>\"\n# 0 \"<command-line>\"\n# 1 " + Quote(operation.source) + "\nint selected();\n";

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
