using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class SourceDiagnosticTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, 16, "BGCS-CSHARP-ABI-PLAN")]
    [InlineData(BindingImportMode.DllImport, 16, "BGCS-CSHARP-ABI-PLAN")]
    [InlineData(BindingImportMode.LibraryImport, 16, "BGCS-CSHARP-ABI-PLAN")]
    [InlineData(BindingImportMode.FunctionTable, 32, "BGCS-C-ABI")]
    [InlineData(BindingImportMode.DllImport, 32, "BGCS-C-ABI")]
    [InlineData(BindingImportMode.LibraryImport, 32, "BGCS-C-ABI")]
    public async Task RejectedByValueAbiReportsTheOriginalHeaderAndStageCode(BindingImportMode importMode, int alignment, string expectedCode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-abi-location-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "abi.h"), $$"""
                struct __attribute__((aligned({{alignment}}))) OverAligned { int value; };
                struct OverAligned exchange(struct OverAligned value);
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "location", nativeName = "unused" },
                source = new() { language = SourceLanguage.C, headers = ["abi.h"], parseMacros = false },
                api = new() { @namespace = "Generated.Location", name = "LocationApi", importMode = importMode },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult result = await host.GenerateAsync(new(configuration, root));
            Assert.False(result.success);
            BindingDiagnostic[] failures = result.diagnostics.Where(diagnostic => diagnostic.isFailure).ToArray();
            Assert.Equal(alignment == 32 ? 2 : 1, failures.Length);
            foreach (BindingDiagnostic diagnostic in failures)
            {
                Assert.Equal(expectedCode, diagnostic.code);
                Assert.NotNull(diagnostic.location);
                Assert.EndsWith("abi.h", diagnostic.location.Value.file, StringComparison.Ordinal);
                Assert.Equal(1, diagnostic.location.Value.line);
                Assert.True(diagnostic.location.Value.column > 0);
            }
            Assert.DoesNotContain(result.diagnostics, diagnostic => diagnostic.code == "BGCS-GENERATION-SESSION");
            Assert.Empty(result.artifacts);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
