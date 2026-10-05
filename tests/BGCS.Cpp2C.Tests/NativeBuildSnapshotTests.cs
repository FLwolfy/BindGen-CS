using System;
using System.Collections.Generic;
using BGCS.Cpp2C.Build;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class NativeBuildSnapshotTests
{
    [Fact]
    public void PlansOwnTheirArgumentsAndPipelineClosure()
    {
        List<string> arguments = ["-c", "source.cpp"];
        NativeBuildPlan plan = new("compiler", "clang", arguments, ".", "output.o");
        NativeBuildStep step = new("compile", "clang", arguments, ".");
        List<NativeBuildStep> steps = [step];
        List<NativeBuildInputFile> inputs = [new("source.cpp", "int value;")];
        NativeBuildPipeline pipeline = new("compiler", inputs, steps, "output.o");

        arguments.Clear();
        steps.Clear();
        inputs.Clear();

        Assert.Equal(new[] { "-c", "source.cpp" }, plan.arguments);
        Assert.Equal(plan.arguments, pipeline.steps[0].arguments);
        Assert.Single(pipeline.inputFiles);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)plan.arguments)[0] = "--changed");
        Assert.Throws<NotSupportedException>(() => ((IList<NativeBuildStep>)pipeline.steps).Clear());
    }

    [Fact]
    public void ManifestAndInspectionResultsOwnTheirCollections()
    {
        List<string> sources = ["source.cpp"];
        CppBridgeBuildManifest manifest = new("windows-x64-msvc", null, null, null,
            "c++23", "sample", sources, [], [], [], [], [], [], [], [], []);
        List<string> replacement = ["replacement.cpp"];
        CppBridgeBuildManifest updated = manifest with { sourceFiles = replacement };
        NativeExportInspectionResult result = new("dumpbin", sources, sources, []);

        sources.Clear();
        replacement.Clear();

        Assert.Equal("source.cpp", Assert.Single(manifest.sourceFiles));
        Assert.Equal("replacement.cpp", Assert.Single(updated.sourceFiles));
        Assert.Equal(manifest.sourceFiles, result.expected);
        Assert.Equal(manifest.sourceFiles, result.actual);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)updated.sourceFiles).Clear());
    }
}
