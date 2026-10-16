using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.IO;
using BGCS.NativeBuild.Execution;
using BGCS.NativeBuild.Planning;
using BGCS.NativeBuild.Validation;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeRecordTraitObserverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualTargetObjectSuppliesNativeTraitsAndRetainsExternalMutationObligations(bool anonymousTypedef)
    {
        using Fixture fixture = new(anonymousTypedef: anonymousTypedef);
        var prepared = await fixture.Prepare();
        NativeRecordTraitObservation observed = await fixture.Observe(prepared);
        NativeRecordTraitFact fact = Assert.Single(observed.facts);
        Assert.Equal(new("record-declaration"), fact.declaration);
        Assert.True(fact.isDefaultConstructible);
        Assert.True(fact.isStandardLayout);
        Assert.True(fact.isTriviallyCopyable);
        Assert.False(fixture.module.declarations.Single().isStandardLayout);
        Assert.NotNull(observed.evidence);
        Assert.Equal(NativeOutputKind.Object, observed.evidence.plan.outputKind);
        Assert.Equal(0, observed.evidence.inspection.exitCode);
        Assert.Contains(observed.inputs.SelectMany(snapshot => snapshot.files), input => input.path == fixture.header);
        Assert.DoesNotContain(observed.inputs.SelectMany(snapshot => snapshot.files), input => input.path.StartsWith(prepared.plan.scratchDirectory!, StringComparison.Ordinal));
        Assert.All(observed.inputs, snapshot => Assert.True(SourceSnapshotVerifier.Matches(snapshot)));
        DateTime timestamp = File.GetLastWriteTimeUtc(fixture.header);
        long length = new FileInfo(fixture.header).Length;
        string original = await File.ReadAllTextAsync(fixture.header);
        await File.WriteAllTextAsync(fixture.header, original.Replace("int   x", "float x", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(fixture.header, timestamp);
        Assert.Equal(length, new FileInfo(fixture.header).Length);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(fixture.header));
        Assert.Contains(observed.inputs, snapshot => !SourceSnapshotVerifier.Matches(snapshot));
        Assert.False(Directory.Exists(Path.Combine(fixture.root, "query")));
    }

    [Fact]
    public async Task CompleteNestedRecordInsideUnboundTemplateIsNotAnAddressableTraitQuery()
    {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, "template<class T> struct Outer { struct Nested { int x; }; };\n");
        var prepared = await fixture.Prepare();
        StableId owner = new("unbound-owner"), parameter = new("template-parameter");
        NativeDeclaration child = fixture.module.declarations.Single() with { name = "Nested", qualifiedName = "Outer::Nested", parent = owner };
        NativeModule module = new(fixture.module.targetId, fixture.module.triple, fixture.module.pointerSize,
            fixture.module.types.Select(type => type with { spelling = "Outer::Nested" }).Append(new(parameter, NativeTypeCategory.Template, "T", null, null)).ToImmutableArray(),
            [new(owner, "Class", "Outer", "Outer", null, new(fixture.header, 1, 1)) { templateKind = "TemplateClass", templateParameters = [parameter], children = [child.id] }, child], [])
        { language = SourceLanguage.Cpp };
        NativeRecordTraitObservation observed = await new NativeRecordTraitObserver().ObserveAsync(prepared.plan, prepared.operation, prepared.digest, module,
            Path.Combine(fixture.root, "query"), TimeSpan.FromSeconds(90));
        Assert.Empty(observed.facts);
        Assert.Null(observed.evidence);
        Assert.False(Directory.Exists(Path.Combine(fixture.root, "query")));
    }

    [Fact]
    public async Task CompletedPreprocessingDigestCannotBeReplacedBeforeAcquisition()
    {
        using Fixture fixture = new();
        var prepared = await fixture.Prepare();
        byte[] bytes = await File.ReadAllBytesAsync(prepared.operation.outputFile);
        DateTime timestamp = File.GetLastWriteTimeUtc(prepared.operation.outputFile);
        bytes[^2] ^= 1;
        await File.WriteAllBytesAsync(prepared.operation.outputFile, bytes);
        File.SetLastWriteTimeUtc(prepared.operation.outputFile, timestamp);
        IOException failure = await Assert.ThrowsAsync<IOException>(async () => await fixture.Observe(prepared));
        Assert.Contains("Completed native preprocessing bytes changed", failure.Message);
        Assert.False(Directory.Exists(Path.Combine(fixture.root, "query")));
    }

    [Fact]
    public async Task LtoCannotSilentlySubstituteAnUninspectableObject()
    {
        using Fixture fixture = new();
        var prepared = await fixture.Prepare();
        NativePreprocessingOperation operation = prepared.operation with { command = prepared.operation.command with { arguments = prepared.operation.command.arguments.Add("-flto") } };
        NativeBuildPlan plan = prepared.plan with { preprocessingOperations = [operation] };
        NotSupportedException failure = await Assert.ThrowsAsync<NotSupportedException>(async () => await fixture.Observe((plan, operation, prepared.digest)));
        Assert.Contains("non-LTO target object", failure.Message);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("size")]
    [InlineData("malformed")]
    public async Task ActualCompiledObjectCannotBeAcceptedWithInvalidInspectorSymbols(string corruption)
    {
        using Fixture fixture = new();
        var prepared = await fixture.Prepare();
        string controls = Path.Combine(fixture.root, "inspector");
        Directory.CreateDirectory(controls);
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        Assert.NotNull(repository);
        string configuration = typeof(NativeRecordTraitObserverTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string original = Path.Combine(repository.FullName, "tests", "fixtures", "hosts", "ProcessProbe", "bin", configuration, "net10.0");
        foreach (string path in Directory.GetFiles(original).Where(path => Path.GetFileName(path).StartsWith("ProcessProbe", StringComparison.Ordinal) && !path.EndsWith(".pdb", StringComparison.Ordinal)))
            File.Copy(path, Path.Combine(controls, Path.GetFileName(path)));
        string stem = "bgcs_native_record_trait_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("record-declaration")));
        string[] valid = Enumerable.Range(0, 3).Select(index => stem + "_" + index + " D 0 2").ToArray();
        string text = corruption switch
        {
            "missing" => string.Join('\n', valid.Take(2)),
            "duplicate" => string.Join('\n', valid.Append(valid[0])),
            "size" => string.Join('\n', valid.Select((line, index) => index == 0 ? line[..^1] + "3" : line)),
            _ => string.Join('\n', valid.Select((line, index) => index == 0 ? line.Replace(" D 0 2", " D 0 nope", StringComparison.Ordinal) : line))
        };
        await File.WriteAllTextAsync(Path.Combine(controls, "symbols.txt"), text);
        SourceSnapshot support = new ExternalInputSnapshotBuilder().Capture(Directory.GetFiles(controls).Select(path => new DeclaredExternalInput(path, ExternalInputKind.File)).ToImmutableArray(), controls);
        string executable = Path.Combine(controls, OperatingSystem.IsWindows() ? "ProcessProbe.exe" : "ProcessProbe");
        Assert.True(File.Exists(executable), executable);
        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Observe(prepared, executable));
        Assert.Contains(corruption == "missing" ? "omitted a required symbol" : "malformed, duplicate or unsupported symbol size", failure.Message);
        Assert.Equal(64, (await File.ReadAllTextAsync(Path.Combine(controls, "object-observed"))).Length);
        Assert.True(SourceSnapshotVerifier.Matches(support));
        Assert.False(Directory.Exists(Path.Combine(fixture.root, "query")));
    }

    [Fact]
    public async Task SelectedGnuUniqueOwnerTraitsAreObservedWithoutExecutingTheTarget()
    {
        if (!OperatingSystem.IsLinux()) return;
        using Fixture fixture = new(gnu: true);
        await File.WriteAllTextAsync(fixture.header, "#include <memory>\nstruct Observed { std::unique_ptr<int> x; };\n");
        var prepared = await fixture.Prepare();
        NativeRecordTraitObservation observed = await fixture.Observe(prepared);
        NativeRecordTraitFact fact = Assert.Single(observed.facts);
        Assert.True(fact.isDefaultConstructible);
        Assert.False(fact.isTriviallyCopyable);
        Assert.False(fixture.module.declarations.Single().isStandardLayout);
        // The selected GCC is the authority. Recompile an independent assertion
        // using the observed value, never assume that Clang's classification wins.
        string proof = Path.Combine(fixture.root, "check.cpp");
        await File.WriteAllTextAsync(proof, "#include \"" + fixture.header.Replace('\\', '/') + "\"\nstatic_assert(__is_standard_layout(Observed) == " + (fact.isStandardLayout ? "1" : "0") + ");\n");
        NativeBuildPlan check = new CompilerBuildProvider("gnu", fixture.target.toolchain.compilerPath!).Plan(new(fixture.target,
            [new(proof, SourceLanguage.Cpp, "c++23")], Path.Combine(fixture.root, "check"), Path.Combine(fixture.root, "check", "check.o"))
        { outputKind = NativeOutputKind.Object, systemIncludeDirectories = fixture.target.toolchain.systemIncludeFolders.Concat(fixture.target.toolchain.cxxSystemIncludeFolders).ToImmutableArray() }, CancellationToken.None);
        NativeBuildExecutionResult result = await new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(check, TimeSpan.FromSeconds(90));
        Assert.All(result.executions, execution => Assert.Equal(0, execution.exitCode));
        Assert.Equal(NativeOutputKind.Object, check.outputKind);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string m_family;
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-native-record-traits-" + Guid.NewGuid().ToString("N"));
        internal string header { get; }
        internal NativeTargetDescriptor target { get; }
        internal NativeModule module { get; }

        internal Fixture(
            bool gnu = false,
            bool anonymousTypedef = false
        )
        {
            Directory.CreateDirectory(root);
            header = Path.Combine(root, "observed.hpp");
            File.WriteAllText(header, anonymousTypedef ? "typedef struct { int   x; } Observed;\n" : "struct Observed { int   x; };\n");
            m_family = gnu ? "gnu" : "clang";
            NativeTargetDescriptor host = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp, m_family);
            // Data-symbol sizes require ELF, not COFF or Mach-O. The local Clang
            // witness emits a freestanding cross-object and never executes it.
            target = gnu ? host : new(new("trait-elf"), "linux", "x64", "gnu", "x86_64-unknown-linux-gnu",
                new(host.toolchain.compilerPath, compilerVersion: host.toolchain.compilerVersion));
            StableId type = new("record-type"), declaration = new("record-declaration");
            module = new(target.targetId.value, target.triple, 8,
                [new(type, NativeTypeCategory.Record, "Observed", 4, 4) { declaration = declaration }],
                [new(declaration, "Class", "Observed", "Observed", type, new(header, 1, 1))
                { role = "Struct", visibility = "Public", isCompleteDefinition = true, templateKind = "NormalClass", isDefaultConstructible = false, isStandardLayout = false, isTriviallyCopyable = false }], [])
            { language = SourceLanguage.Cpp };
        }

        internal async Task<(NativeBuildPlan plan, NativePreprocessingOperation operation, ContentDigest digest)> Prepare()
        {
            string source = Path.Combine(root, "source.cpp");
            await File.WriteAllTextAsync(source, "#include \"" + header.Replace('\\', '/') + "\"\n");
            string scratch = Path.Combine(root, "preprocessing");
            NativeBuildRequest request = new(target, [new(source, SourceLanguage.Cpp, "c++23")], scratch, Path.Combine(scratch, "unused.o"))
            {
                outputKind = NativeOutputKind.Object, snapshotExcludedDirectories = [scratch, Path.Combine(root, "query"), Path.Combine(root, "inspector")],
                systemIncludeDirectories = target.toolchain.systemIncludeFolders.Concat(target.toolchain.cxxSystemIncludeFolders).ToImmutableArray(),
                compilerArguments = target.toolchain.arguments.ToImmutableArray()
            };
            NativeBuildPlan plan = new CompilerBuildProvider(m_family,
                target.toolchain.compilerPath!, linker: m_family == "gnu" ? null : target.toolchain.linkerPath).Plan(request, CancellationToken.None) with { snapshotExcludedDirectories = request.snapshotExcludedDirectories };
            await new PlannedNativeBuildExecutor().PreprocessWithDependenciesAsync(plan, TimeSpan.FromSeconds(90));
            NativePreprocessingOperation operation = Assert.Single(plan.preprocessingOperations);
            return (plan, operation, new(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(operation.outputFile)))));
        }

        internal ValueTask<NativeRecordTraitObservation> Observe(
            (NativeBuildPlan plan, NativePreprocessingOperation operation, ContentDigest digest) prepared,
            string? inspector = null
        ) => new NativeRecordTraitObserver().ObserveAsync(prepared.plan, prepared.operation, prepared.digest, module,
            Path.Combine(root, "query"), TimeSpan.FromSeconds(90), symbolToolPath: inspector);

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
