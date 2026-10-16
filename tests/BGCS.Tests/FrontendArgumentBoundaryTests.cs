using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using Xunit;

namespace BGCS.Tests;

public sealed class FrontendArgumentBoundaryTests
{
    [Fact]
    public async Task HostExpandsConfigPrecedenceOnceAndPreservesOriginalToolchainProvenance()
    {
        string temporary = Temporary();
        try
        {
            string config = Path.Combine(temporary, "explicit.cfg");
            string response = Path.Combine(temporary, "source.rsp");
            File.WriteAllText(config, "-DNATIVE_VALUE=1\n$-Wl,-Bstatic\n");
            File.WriteAllText(response, "-DNATIVE_VALUE=3\n");
            CapturingFrontend frontend = new();
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithFrontendFactory(() => frontend).Build();
            AnalysisResult result = await host.AnalyzeAsync(Request(temporary) with
            {
                toolchain = new NativeToolchainDescriptor(arguments: ["-DNATIVE_VALUE=2", "--config=" + config]),
                arguments = ["@" + response]
            });
            Assert.True(result.success, result.GetDiagnosticSummary());
            FrontendRequest parsed = Assert.IsType<FrontendRequest>(frontend.request);
            Assert.Equal(new[] { "-DNATIVE_VALUE=1", "-DNATIVE_VALUE=2", "-DNATIVE_VALUE=3" }, parsed.arguments.ToArray());
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
    public async Task ExpandedIdentitySubstitutionFailsBeforeFrontendCreation(string options)
    {
        string temporary = Temporary();
        try
        {
            string config = Path.Combine(temporary, "hidden.cfg");
            File.WriteAllText(config, options);
            bool created = false;
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithFrontendFactory(() =>
            {
                created = true;
                return new CapturingFrontend();
            }).Build();
            AnalysisResult result = await host.AnalyzeAsync(Request(temporary) with { arguments = ["--config=" + config] });
            Assert.False(result.success);
            Assert.False(created);
            Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "BGCS-ANALYSIS-SESSION"
                && diagnostic.message.Contains("Effective frontend arguments", StringComparison.Ordinal));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static AnalysisRequest Request(string pathBase) => AnalysisRequest.FromFiles(["native.h"], baseDirectory: pathBase) with
    { targetId = "emscripten-wasm32-emscripten" };

    private static string Temporary()
    {
        string path = Path.Combine(Path.GetTempPath(), "bgcs-frontend-arguments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CapturingFrontend : IFrontend
    {
        internal FrontendRequest? request;
        public ValueTask<FrontendResult> ParseAsync(FrontendRequest input, CancellationToken cancellationToken)
        {
            request = input;
            NativeModule module = new(input.target.targetId.value, input.target.triple, 4, [], [], []) { language = input.language };
            return ValueTask.FromResult(new FrontendResult(module, new SourceSnapshot([], [])));
        }
    }
}
