using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.NativeBuild.Execution;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativePreprocessingOnlyTests
{
    [Fact]
    public async Task MsvcFactPreflightPreservesParserOnlyExactScalarDeductionWithoutCompilingIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs MSVC preprocessing-only facts " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "original.h"), source = Path.Combine(root, "native.c"), facts = Path.Combine(root, "facts.c");
            await File.WriteAllTextAsync(header, "#define EXACT_NATIVE_VALUE 18446744073709551615ULL\nint native_value(void);\n");
            await File.WriteAllTextAsync(source, "#include \"original.h\"\nint native_value(void) { return 42; }\n");
            await File.WriteAllTextAsync(facts, "#include \"original.h\"\nconst __auto_type bgcs_scalar_fact = (EXACT_NATIVE_VALUE);\n");
            var target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("windows-x64-msvc"))), SourceLanguage.C, "msvc");
            var sdk = MsvcToolchainDiscovery.Resolve(target.toolchain.compilerPath!, "x64", target.toolchain.sysRoot, target.toolchain.sdkVersion);
            string scratch = Path.Combine(root, "scratch");
            NativeBuildRequest request = new(target, [new(source, SourceLanguage.C, "c17")], scratch, Path.Combine(scratch, "native.obj"))
            { outputKind = NativeOutputKind.Object, preprocessingSources = [new(facts, SourceLanguage.C, "c17")] };
            NativeBuildPlan plan = new MsvcBuildProvider(sdk).Plan(request, CancellationToken.None);
            NativeDependencyScan factScan = Assert.Single(plan.dependencyScans, scan => scan.dependencyFile.source == facts);
            Assert.Contains("/P", factScan.command.arguments);
            Assert.DoesNotContain("/Zs", factScan.command.arguments);
            Assert.Contains("/Zs", Assert.Single(plan.dependencyScans, scan => scan.dependencyFile.source == source).command.arguments);
            int verified = 0;
            NativeBuildExecutionResult execution = await new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(plan, TimeSpan.FromSeconds(60),
                sourceVerifier: async (operation, cancellationToken) =>
                {
                    Assert.True(operation.preprocessingOnly);
                    string expanded = await File.ReadAllTextAsync(operation.outputFile, cancellationToken);
                    Assert.Contains("const__auto_typebgcs_scalar_fact=(18446744073709551615ULL)",
                        string.Concat(expanded.Where(character => !char.IsWhiteSpace(character))));
                    verified++;
                }, verificationSources: [facts]);
            Assert.Equal(1, verified);
            Assert.True(File.Exists(plan.outputFile));
            Assert.Contains(execution.inputSnapshot.files, input => input.path.Equals(header, StringComparison.OrdinalIgnoreCase));
            Assert.Single(Directory.GetFiles(scratch, "*.obj", SearchOption.AllDirectories));
            Assert.DoesNotContain(plan.commands, command => command.inputFiles.Contains(facts));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
