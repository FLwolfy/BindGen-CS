using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Calls;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Interop.DotNet;
using Xunit;

namespace BGCS.Tests;

public sealed class SourceDiagnosticTests
{
    [Fact]
    public async Task FrontendFailureDetailsRemainStructuredWithoutExpandingTheSummary()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-failure-details-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "int read_value(void);\n");
            await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new FailedFrontend()).Build();
            AnalysisResult result = await host.AnalyzeAsync(new(["native.h"], SourceLanguage.C, root) { parseMacros = false });
            BindingDiagnostic failure = Assert.Single(result.diagnostics, diagnostic => diagnostic.code == "BGCS-ANALYSIS-SESSION");
            Assert.Equal("fixture worker summary", failure.message);
            Assert.Equal("fixture worker throw-site\n at Collection.insert", failure.details);
            string json = JsonSerializer.Serialize(failure);
            Assert.Equal(failure.details, JsonSerializer.Deserialize<BindingDiagnostic>(json)!.details);
            Assert.DoesNotContain("\"details\"", JsonSerializer.Serialize(new BindingDiagnostic(BindingDiagnosticSeverity.Error, "ordinary failure")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class FailedFrontend : IFrontend
    {
        public ValueTask<FrontendPreparation> PrepareAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new FrontendPreparation(request, new([], []), []));
        }

        public ValueTask<FrontendResult> ParseAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        )
            => ValueTask.FromException<FrontendResult>(new InvalidDataException("fixture worker summary", new IOException("fixture worker throw-site\n at Collection.insert")));
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable, 16, "BGCS-CALL-ABI")]
    [InlineData(BindingImportMode.DllImport, 16, "BGCS-CALL-ABI")]
    [InlineData(BindingImportMode.LibraryImport, 16, "BGCS-CALL-ABI")]
    [InlineData(BindingImportMode.FunctionTable, 32, "BGCS-C-ABI")]
    [InlineData(BindingImportMode.DllImport, 32, "BGCS-C-ABI")]
    [InlineData(BindingImportMode.LibraryImport, 32, "BGCS-C-ABI")]
    public async Task RejectedByValueAbiReportsTheOriginalHeaderAndStageCode(
        BindingImportMode importMode,
        int alignment,
        string expectedCode
    ) {
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
                schemaVersion = 2,
                library = new() { id = "location", nativeName = "unused" },
                source = new() { language = SourceLanguage.C, headers = ["abi.h"], parseMacros = false },
                bridge = new() { aggregateStrategy = AggregateCallStrategy.Direct },
                api = new() { @namespace = "Generated.Location", name = "LocationApi", importMode = importMode },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
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
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedAbiRejectionIdentityCannotReplaceAValidPublication(bool contradictDirect)
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bgcs-abi-rejection-identity-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "int identity(int value);\n");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "rejection_identity", nativeName = "unused" },
                source = new() { language = SourceLanguage.C, headers = ["native.h"], parseMacros = false },
                cache = new() { enabled = false }
            };
            await using BindingHost valid = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult first = await valid.GenerateAsync(new(configuration, root));
            Assert.True(first.success, first.GetDiagnosticSummary());
            string[] before = PublicationBytes(first.outputRoot);
            await using BindingHost invalid = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot())
                .ReplaceFactory<ICallAbiProvider>("dotnet.call-abi", new("fixture.bad-abi", ExtensionPort.CallAbiProvider),
                    _ => new MalformedAbiProvider(contradictDirect)).Build();
            GenerationResult failure = await invalid.GenerateAsync(new(configuration, root));
            Assert.False(failure.success);
            Assert.False(failure.published);
            Assert.Contains("foreign type or contradicted", failure.GetDiagnosticSummary(), StringComparison.Ordinal);
            Assert.Equal(before, PublicationBytes(first.outputRoot));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string[] PublicationBytes(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(directory, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();

    private sealed class MalformedAbiProvider(bool contradictDirect) : ICallAbiProvider
    {
        public CallAbiAssessment Assess(CallAbiRequest request)
        {
            CallAbiAssessment actual = new DotNetCallAbiProvider().Assess(request);
            if (actual.disposition == NativeCallDisposition.NotApplicable) return actual;
            return contradictDirect
                ? actual with { rejectionType = request.signature.returnType }
                : new(actual.provider, NativeCallDisposition.Unsupported, "fixture rejection") { rejectionType = new StableId("foreign.type") };
        }
    }
}
