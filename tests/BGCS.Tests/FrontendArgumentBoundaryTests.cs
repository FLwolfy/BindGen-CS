using System;
using BGCS.Core.Hosting;
using BGCS.Frontend.Clang;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using Xunit;

namespace BGCS.Tests;

public sealed class FrontendArgumentBoundaryTests
{
    [Fact]
    public async Task FrontendExpandsConfigPrecedenceOnceAndPreservesOriginalToolchainProvenance()
    {
        string temporary = Temporary();
        try
        {
            string config = Path.Combine(temporary, "explicit.cfg");
            string response = Path.Combine(temporary, "source.rsp");
            File.WriteAllText(config, "-DNATIVE_VALUE=1\n$-Wl,-Bstatic\n");
            File.WriteAllText(response, "-DNATIVE_VALUE=3\n");
            CapturingFrontend frontend = new();
            await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => frontend).Build();
            AnalysisResult result = await host.AnalyzeAsync(Request(temporary) with
            {
                toolchain = new NativeToolchainDescriptor(arguments: ["-DNATIVE_VALUE=2", "--config=" + config]),
                arguments = ["@" + response]
            });
            Assert.True(result.success, result.GetDiagnosticSummary());
            FrontendRequest parsed = Assert.IsType<FrontendRequest>(frontend.request);
            string[] selectedToolchainArguments = result.target!.toolchain.arguments.Where(value => value != "--config=" + config).ToArray();
            Assert.Equal(new[] { "-DNATIVE_VALUE=1" }.Concat(selectedToolchainArguments).Append("-DNATIVE_VALUE=3"), parsed.arguments);
            Assert.Equal(new[] { "-DNATIVE_VALUE=1", "-DNATIVE_VALUE=2", "-DNATIVE_VALUE=3" },
                parsed.arguments.Where(value => value.StartsWith("-DNATIVE_VALUE=", StringComparison.Ordinal)));
            Assert.Empty(parsed.target.toolchain.arguments);
            Assert.Contains("--config=" + config, result.target!.toolchain.arguments);
            Assert.Equal(2, result.argumentInputs!.files.Length);
            Assert.DoesNotContain(parsed.arguments, value => value.StartsWith('@') || value.StartsWith("-Xlinker", StringComparison.Ordinal));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Theory]
    [InlineData("-Xclang -target-abi -Xclang alternate")]
    [InlineData("$--target=x86_64-linux-musl")]
    [InlineData("-Xclang -load -Xclang native-plugin.dll")]
    public async Task ExpandedIdentitySubstitutionFailsBeforeFrontendParsing(string options)
    {
        string temporary = Temporary();
        try
        {
            string config = Path.Combine(temporary, "hidden.cfg");
            File.WriteAllText(config, options);
            bool created = false;
            CapturingFrontend? frontend = null;
            await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() =>
            {
                created = true;
                return frontend = new CapturingFrontend();
            }).Build();
            AnalysisResult result = await host.AnalyzeAsync(Request(temporary) with { arguments = ["--config=" + config] });
            Assert.False(result.success);
            Assert.True(created);
            Assert.Null(frontend!.request);
            Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "BGCS-ANALYSIS-SESSION"
                && diagnostic.message.Contains("Effective Clang frontend arguments", StringComparison.Ordinal)
                && diagnostic.message.Contains(options.Contains("-load", StringComparison.Ordinal) ? "opaque native AST plugins" : "resolved CPU", StringComparison.Ordinal));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static AnalysisRequest Request(string pathBase) => AnalysisRequest.FromFiles(["native.h"], baseDirectory: pathBase) with
    { targetId = "host" };

    private static string Temporary()
    {
        string path = Path.Combine(Path.GetTempPath(), "bgcs-frontend-arguments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CapturingFrontend : IFrontend
    {
        public ValueTask<FrontendPreparation> PrepareAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        ) {
            using HostServices host = TestHostPaths.CreateHost();
            return new ClangFrontend(host).PrepareAsync(request, cancellationToken);
        }

        internal FrontendRequest? request;
        public ValueTask<FrontendResult> ParseAsync(
            FrontendRequest input,
            CancellationToken cancellationToken
        ) {
            request = input;
            NativeModule module = new(input.target.targetId.value, input.target.triple, TestComposition.PointerSize(input.target), [], [], []) { language = input.language, byteOrder = input.target.byteOrder };
            return ValueTask.FromResult(new FrontendResult(module, new SourceSnapshot([], [])));
        }
    }
}
