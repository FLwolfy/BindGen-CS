using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.IO;
using BGCS.NativeBuild.Execution;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeResponseFileTests
{
    [Theory]
    [InlineData("--target=x86_64-linux-musl")]
    [InlineData("-Xclang -target-abi -Xclang unsupported")]
    [InlineData("-msoft-float")]
    [InlineData("/Gv")]
    [InlineData("/Gr")]
    [InlineData("/Gz")]
    [InlineData("-mrtd")]
    [InlineData("-msseregparm")]
    [InlineData("-mregparm=3")]
    [InlineData("-Xclang -fdefault-calling-conv=vectorcall")]
    [InlineData("/CLANG:/Gv")]
    [InlineData("-fcall-used-rbx")]
    [InlineData("-fcall-saved-rax")]
    public void NestedResponseCannotReplaceTheFrozenTargetOrCallingAbi(string option)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-hidden-abi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string source = Path.Combine(temporary, "source.c"), response = Path.Combine(temporary, "hidden.rsp");
            File.WriteAllText(source, "int native_roundtrip(int value) { return value; }\n");
            File.WriteAllText(response, option + "\n");
            var target = new TargetResolver().Resolve(new(new("windows-x64-msvc")));
            NativeBuildRequest request = new(target, [new(source, SourceLanguage.C, "c17")], temporary, Path.Combine(temporary, "never-created.obj"))
                { outputKind = NativeOutputKind.Object, compilerArguments = ["@" + response] };
            ArgumentException failure = Assert.Throws<ArgumentException>(() => new CompilerBuildProvider("clang", "never-run-clang").Plan(request, CancellationToken.None));
            Assert.Contains("resolved CPU", failure.Message);
            Assert.False(File.Exists(request.outputFile));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void NativeAstPluginsAreRejectedBeforeCompilerDependencyOrPreprocessingStartup()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-native-ast-plugin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string source = Path.Combine(temporary, "source.c");
            File.WriteAllText(source, "int native_roundtrip(int value) { return value; }\n");
            var target = new TargetResolver().Resolve(new(new("windows-x64-msvc")));
            NativeBuildRequest request = new(target, [new(source, SourceLanguage.C, "c17")], temporary, Path.Combine(temporary, "never-created.obj"))
                { outputKind = NativeOutputKind.Object, compilerArguments = ["-Xclang", "-load", "-Xclang", "mutate-abi.dll"] };
            NotSupportedException failure = Assert.Throws<NotSupportedException>(() => new CompilerBuildProvider("clang", "never-run-clang").Plan(request, CancellationToken.None));
            Assert.Contains("native AST plugins", failure.Message);
            Assert.False(File.Exists(request.outputFile));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task NestedQuotedResponsesAreLockedAtPlanningAndMutationPreventsAnyCompilerInvocation()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs response closure & " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string source = Path.Combine(temporary, "source.c"), compiler = Path.Combine(temporary, "never-run-clang.exe");
            string top = Path.Combine(temporary, "top.rsp"), nested = Path.Combine(temporary, "nested response.rsp");
            await File.WriteAllTextAsync(source, "int native_roundtrip(int value) { return value; }\n");
            await File.WriteAllTextAsync(compiler, "not an executable; dependency verification must reject before tool startup");
            await File.WriteAllTextAsync(top, "@\"" + nested.Replace('\\', '/') + "\"\n");
            await File.WriteAllTextAsync(nested, "-DNATIVE_FACTOR=2\n");
            var target = new TargetResolver().Resolve(new(new("windows-x64-msvc")));
            NativeBuildRequest request = new(target, [new(source, SourceLanguage.C, "c17")], temporary, Path.Combine(temporary, "never-created.obj"))
            { outputKind = NativeOutputKind.Object, compilerArguments = ["@" + top] };
            NativeBuildPlan plan = new CompilerBuildProvider("clang", compiler).Plan(request, CancellationToken.None);
            Assert.Equal(2, plan.argumentInputs.files.Length);
            Assert.Contains(plan.argumentInputs.files, file => file.path == nested);
            Assert.True(SourceSnapshotVerifier.Matches(plan.argumentInputs));
            await File.AppendAllTextAsync(nested, "-DNATIVE_FACTOR=3\n");
            Assert.False(SourceSnapshotVerifier.Matches(plan.argumentInputs));
            IOException failure = await Assert.ThrowsAsync<IOException>(() => new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(plan, TimeSpan.FromSeconds(10)));
            Assert.Contains("response-file argument inputs changed after the plan was frozen", failure.Message);
            Assert.False(File.Exists(plan.outputFile));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void ResponseCyclesAreRejectedDuringPurePlanning()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs response-cycle " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string source = Path.Combine(temporary, "source.c"), top = Path.Combine(temporary, "top.rsp"), nested = Path.Combine(temporary, "nested.rsp");
            File.WriteAllText(source, "int native_roundtrip(int value) { return value; }\n");
            File.WriteAllText(top, "@\"" + nested.Replace('\\', '/') + "\"");
            File.WriteAllText(nested, "@\"" + top.Replace('\\', '/') + "\"");
            var target = new TargetResolver().Resolve(new(new("windows-x64-msvc")));
            NativeBuildRequest request = new(target, [new(source, SourceLanguage.C, "c17")], temporary, Path.Combine(temporary, "never-created.obj"))
            { outputKind = NativeOutputKind.Object, compilerArguments = ["@" + top] };
            Assert.Contains("recursive inclusion cycle", Assert.Throws<InvalidDataException>(() => new CompilerBuildProvider("clang", "never-run-clang").Plan(request, CancellationToken.None)).Message);
            Assert.False(File.Exists(request.outputFile));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task ClangConfigurationsLockNestedCfgdirIncludesAndMutationPreventsCompilerStartup()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs config closure & " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string configurationDirectory = Path.Combine(temporary, "configuration directory");
            Directory.CreateDirectory(configurationDirectory);
            string source = Path.Combine(temporary, "source.c"), compiler = Path.Combine(temporary, "never-run-clang.exe");
            string top = Path.Combine(configurationDirectory, "top.cfg"), nested = Path.Combine(configurationDirectory, "nested options.cfg");
            await File.WriteAllTextAsync(source, "int native_roundtrip(int value) { return value; }\n");
            await File.WriteAllTextAsync(compiler, "must not run");
            await File.WriteAllTextAsync(top, "# leading comment\n-DNATIVE_TEXT=\"#literal\" \\\n @\"<CFGDIR>/nested options.cfg\" # ignored trailing comment\n");
            await File.WriteAllTextAsync(nested, "-DNATIVE_FACTOR=2\n");
            var target = new TargetResolver().Resolve(new(new("windows-x64-msvc")));
            NativeBuildRequest request = new(target, [new(source, SourceLanguage.C, "c17")], temporary, Path.Combine(temporary, "never-created.obj"))
            { outputKind = NativeOutputKind.Object, compilerArguments = ["--config=" + top] };
            NativeBuildPlan plan = new CompilerBuildProvider("clang", compiler).Plan(request, CancellationToken.None);
            Assert.Equal(2, plan.argumentInputs.files.Length);
            Assert.Contains(plan.argumentInputs.files, file => file.path == nested);
            await File.AppendAllTextAsync(nested, "-DNATIVE_FACTOR=3\n");
            IOException failure = await Assert.ThrowsAsync<IOException>(() => new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(plan, TimeSpan.FromSeconds(10)));
            Assert.Contains("response-file argument inputs changed", failure.Message);
            Assert.False(File.Exists(plan.outputFile));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void ShortClangConfigNamesTrackHigherPrecedenceAbsenceAndRejectUnknownDefaultSearchRoots()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs config lookup " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string user = Path.Combine(temporary, "user"), compilerDirectory = Path.Combine(temporary, "compiler");
            Directory.CreateDirectory(user);
            Directory.CreateDirectory(compilerDirectory);
            string source = Path.Combine(temporary, "source.c"), compiler = Path.Combine(compilerDirectory, "never-run-clang.exe");
            string config = Path.Combine(compilerDirectory, "controlled.cfg"), shadow = Path.Combine(user, "controlled.cfg");
            File.WriteAllText(source, "int native_roundtrip(int value) { return value; }\n");
            File.WriteAllText(compiler, "must not run");
            File.WriteAllText(config, "-DNATIVE_FACTOR=2\n");
            var target = new TargetResolver().Resolve(new(new("windows-x64-msvc")));
            NativeBuildRequest request = new(target, [new(source, SourceLanguage.C, "c17")], temporary, Path.Combine(temporary, "never-created.obj"))
            { outputKind = NativeOutputKind.Object, compilerArguments = ["--config=controlled.cfg"] };
            Assert.Contains("explicit --config-user-dir", Assert.Throws<InvalidDataException>(() => new CompilerBuildProvider("clang", compiler).Plan(request, CancellationToken.None)).Message);
            NativeBuildPlan plan = new CompilerBuildProvider("clang", compiler).Plan(request with
            { compilerArguments = ["--config=controlled.cfg", "--config-user-dir=" + user, "--config-system-dir="] }, CancellationToken.None);
            Assert.Contains(plan.argumentInputs.absentInputs, missing => missing.path == shadow);
            Assert.True(SourceSnapshotVerifier.Matches(plan.argumentInputs));
            File.WriteAllText(shadow, "-DNATIVE_FACTOR=99\n");
            Assert.False(SourceSnapshotVerifier.Matches(plan.argumentInputs));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void RecursiveClangConfigIncludesFailBeforeAnyToolIsPlanned()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs config-cycle " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string source = Path.Combine(temporary, "source.c"), config = Path.Combine(temporary, "recursive.cfg");
            File.WriteAllText(source, "int native_roundtrip(int value) { return value; }\n");
            File.WriteAllText(config, "@recursive.cfg\n");
            var target = new TargetResolver().Resolve(new(new("windows-x64-msvc")));
            NativeBuildRequest request = new(target, [new(source, SourceLanguage.C, "c17")], temporary, Path.Combine(temporary, "never-created.obj"))
            { outputKind = NativeOutputKind.Object, compilerArguments = ["--config=" + config] };
            Assert.Contains("recursive inclusion cycle", Assert.Throws<InvalidDataException>(() => new CompilerBuildProvider("clang", "never-run-clang").Plan(request, CancellationToken.None)).Message);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
    [Theory]
    [InlineData("clang")]
    [InlineData("gnu")]
    [InlineData("msvc")]
    public async Task InstalledDriversMatchFrozenNestedResponseAndConfigPathBases(string family)
    {
        if (family == "gnu" && !OperatingSystem.IsLinux() || family == "msvc" && !OperatingSystem.IsWindows()) return;
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs argument origin 验收 & " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string origin = Path.Combine(temporary, "invocation"), configurations = Path.Combine(temporary, "configuration directory"), scratch = Path.Combine(temporary, "objects");
            Directory.CreateDirectory(origin); Directory.CreateDirectory(configurations);
            string source = Path.Combine(temporary, "source.c");
            await File.WriteAllTextAsync(source, "_Static_assert(NATIVE_VALUE == 7, \"ordinary response uses invocation directory\");\nint native_roundtrip(int value) { return value; }\n");
            string compiler = family == "msvc" ? MsvcToolchainDiscovery.FindCompiler(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64")!
                : ToolchainDiscovery.FindCompiler(SourceLanguage.C, family == "gnu" ? "gcc" : "clang") ?? ToolchainDiscovery.FindCompiler(SourceLanguage.C)!;
            Assert.False(string.IsNullOrWhiteSpace(compiler));
            var target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"), new(compiler))), SourceLanguage.C, family);
            string top = Path.Combine(configurations, "top.rsp"), actualNested = Path.Combine(origin, "nested response.rsp"), wrongNested = Path.Combine(configurations, "nested response.rsp");
            await File.WriteAllTextAsync(top, "@\"nested response.rsp\"\n");
            await File.WriteAllTextAsync(actualNested, family == "msvc" ? "/DNATIVE_VALUE=7\n" : "-DNATIVE_VALUE=7\n");
            await File.WriteAllTextAsync(wrongNested, family == "msvc" ? "/DNATIVE_VALUE=9\n" : "-DNATIVE_VALUE=9\n");
            NativeBuildRequest request = new(target, [new(source, SourceLanguage.C, "c17")], scratch, Path.Combine(scratch, OperatingSystem.IsWindows() ? "response.obj" : "response.o"))
            { outputKind = NativeOutputKind.Object, argumentWorkingDirectory = origin, compilerArguments = ["@" + top] };
            var provider = new NativeBuildProviderResolver().Resolve(target, family);
            NativeBuildPlan plan = provider.Plan(request, CancellationToken.None);
            Assert.Contains(plan.argumentInputs.files, input => input.path == actualNested);
            Assert.DoesNotContain(plan.argumentInputs.files, input => input.path == wrongNested);
            NativeBuildExecutionResult built = await new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(plan, TimeSpan.FromMinutes(2));
            Assert.True(File.Exists(plan.outputFile));
            Assert.True(SourceSnapshotVerifier.Matches(built.argumentInputs));
            if (family != "clang") return;
            await File.WriteAllTextAsync(source, "_Static_assert(NATIVE_VALUE == 9, \"configuration include uses containing directory\");\nint native_roundtrip(int value) { return value; }\n");
            string configuration = Path.Combine(configurations, "top.cfg"), configNested = Path.Combine(configurations, "nested options.cfg"), wrongConfigNested = Path.Combine(origin, "nested options.cfg");
            await File.WriteAllTextAsync(configuration, "@\"<CFGDIR>/nested options.cfg\"\n");
            await File.WriteAllTextAsync(configNested, "-DNATIVE_VALUE=9\n");
            await File.WriteAllTextAsync(wrongConfigNested, "-DNATIVE_VALUE=7\n");
            NativeBuildPlan configPlan = provider.Plan(request with { outputFile = Path.Combine(scratch, OperatingSystem.IsWindows() ? "configuration.obj" : "configuration.o"), compilerArguments = ["--config=" + configuration] }, CancellationToken.None);
            Assert.Contains(configPlan.argumentInputs.files, input => input.path == configNested);
            Assert.DoesNotContain(configPlan.argumentInputs.files, input => input.path == wrongConfigNested);
            NativeBuildExecutionResult configured = await new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(configPlan, TimeSpan.FromMinutes(2));
            Assert.True(File.Exists(configPlan.outputFile));
            Assert.True(SourceSnapshotVerifier.Matches(configured.argumentInputs));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
