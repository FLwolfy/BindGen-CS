using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Calls;
using BGCS.Core.Hosting;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Engine.Tests;

// These tests generate scalar source with explicitly supplied synthetic mechanisms.
// They establish extensibility and absence of platform fallbacks, not native runtime certification.
public sealed class NeutralEngineCompositionTests
{
    [Fact]
    public async Task TwoThenAThirdIndependentTargetAndRuntimeUseTheSameEngineWithoutFoundationAssemblies()
    {
        using Fixture fixture = new();
        Spec first = new("private-one", "vendor-one", "execution.one", 8);
        Spec second = new("private-two", "vendor-two", "execution.two", 4);
        Spec third = new("private-three", "vendor-three", "execution.three", 8);
        BindingHostBuilder builder = Compose(fixture);
        Register(builder, first); Register(builder, second);
        await using BindingHost original = builder.Build();
        await AssertGenerated(original, fixture, first);
        await AssertGenerated(original, fixture, second);
        Register(builder, third);
        GenerationResult absent = await original.GenerateAsync(Request(fixture, third));
        Assert.False(absent.success);
        await using BindingHost extended = builder.Build();
        await AssertGenerated(extended, fixture, third);
        Assert.DoesNotContain(typeof(BindingHost).Assembly.GetReferencedAssemblies(), assembly => assembly.Name is
            "BGCS" or "BGCS.Hosting.Standard" or "BGCS.Targets.Desktop" or "BGCS.Interop.DotNet");
    }

    [Fact]
    public async Task ReplacementIsExplicitAndMissingRuntimeOrAbiNeverObtainsAnImplicitDesktopPlan()
    {
        using Fixture fixture = new();
        Spec original = new("private-old", "vendor-old", "execution.old", 8);
        Spec replacement = new("private-replacement", "vendor-new", "execution.new", 4);
        BindingHostBuilder builder = Compose(fixture);
        Register(builder, original);
        builder.ReplaceFactory<INativeTargetProvider>("fixture.target." + original.id,
            new("fixture.target.replacement", ExtensionPort.NativeTargetProvider), _ => new Target(replacement));
        builder.ReplaceFactory<IManagedRuntimeProvider>("fixture.runtime." + original.id,
            new("fixture.runtime.replacement", ExtensionPort.ManagedRuntimeProvider), _ => new Runtime(replacement));
        builder.ReplaceFactory<ICallAbiProvider>("fixture.abi." + original.id,
            new("fixture.abi.replacement", ExtensionPort.CallAbiProvider), _ => new Abi(replacement));
        await using BindingHost host = builder.Build();
        await AssertGenerated(host, fixture, replacement);
        Assert.False((await host.GenerateAsync(Request(fixture, original))).success);
        BindingHostBuilder missing = Compose(fixture).RegisterFactory<INativeTargetProvider>(
            new("fixture.only-target", ExtensionPort.NativeTargetProvider), _ => new Target(original));
        await using BindingHost withoutRuntime = missing.Build();
        GenerationResult missingRuntime = await withoutRuntime.GenerateAsync(Request(fixture, original));
        Assert.False(missingRuntime.success);
        Assert.Contains(missingRuntime.diagnostics, diagnostic => diagnostic.message.Contains("runtime provider", StringComparison.OrdinalIgnoreCase));
        missing.RegisterFactory<IManagedRuntimeProvider>(new("fixture.only-runtime", ExtensionPort.ManagedRuntimeProvider), _ => new Runtime(original));
        await using BindingHost withoutAbi = missing.Build();
        GenerationResult missingAbi = await withoutAbi.GenerateAsync(Request(fixture, original));
        Assert.False(missingAbi.success);
        Assert.Contains(missingAbi.diagnostics, diagnostic => diagnostic.message.Contains("calling-ABI", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DuplicateDescriptorsAndConflictingProvidersAreRejectedByTheSameGenerationEntryPoint()
    {
        using Fixture fixture = new();
        Spec spec = new("private-duplicate", "vendor", "execution.duplicate", 8);
        BindingHostBuilder builder = Compose(fixture);
        Register(builder, spec);
        Assert.Throws<InvalidOperationException>(() => builder.RegisterFactory<INativeTargetProvider>(
            new("fixture.target." + spec.id, ExtensionPort.NativeTargetProvider), _ => new Target(spec)));
        Assert.Throws<InvalidOperationException>(() => builder.RegisterFactory<IManagedRuntimeProvider>(
            new("fixture.bad-port", ExtensionPort.NativeTargetProvider), _ => new Runtime(spec)));
        builder.RegisterFactory<INativeTargetProvider>(new("fixture.target.conflict", ExtensionPort.NativeTargetProvider), _ => new Target(spec));
        await using BindingHost host = builder.Build();
        GenerationResult result = await host.GenerateAsync(Request(fixture, spec));
        Assert.False(result.success);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.Empty(result.artifacts);
    }

    private static async Task AssertGenerated(
        BindingHost host,
        Fixture fixture,
        Spec spec
    ) {
        GenerationResult result = await host.GenerateAsync(Request(fixture, spec));
        Assert.True(result.success, result.GetDiagnosticSummary());
        var target = Assert.Single(result.targetModels);
        Assert.Equal(spec.id, target.targetId);
        Assert.Equal(spec.pointerSize, target.interop.pointerSize);
        Assert.Single(target.interop.functions);
        Assert.NotEmpty(result.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp"));
        Assert.All(result.capabilities, report => Assert.Equal(new ManagedRuntimeId(spec.runtime), report.runtimeId));
        string text = string.Join('\n', result.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp")
            .Select(artifact => File.ReadAllText(Path.Combine(result.outputRoot, artifact.relativePath))));
        Assert.Contains("Echo", text, StringComparison.Ordinal);
    }
    private static GenerationRequest Request(
        Fixture fixture,
        Spec spec
    ) => new(new()
    {
        schemaVersion = 2,
        library = new() { id = "neutral", nativeName = "neutral_native" },
        source = new() { headers = ["native.h"], language = SourceLanguage.C, parseMacros = false },
        targets = [new() { id = spec.id }],
        api = new() { @namespace = "Generated.Neutral", name = "NeutralApi", runtime = new(spec.runtime) },
        output = new() { root = "generated-" + spec.id },
        cache = new() { enabled = false }
    }, fixture.root);
    private static BindingHostBuilder Compose(Fixture fixture) => BindingHost.CreateBuilder()
        .RegisterFactory<IHostServicesProvider>(new("fixture.host", ExtensionPort.HostServicesProvider), _ => new Host(fixture))
        .RegisterFactory<IFrontend>(new("fixture.frontend", ExtensionPort.Frontend), context => new Frontend(context.RequireHostServices().paths));
    private static void Register(
        BindingHostBuilder builder,
        Spec spec
    ) {
        builder.RegisterFactory<INativeTargetProvider>(new("fixture.target." + spec.id, ExtensionPort.NativeTargetProvider), _ => new Target(spec));
        builder.RegisterFactory<IManagedRuntimeProvider>(new("fixture.runtime." + spec.id, ExtensionPort.ManagedRuntimeProvider), _ => new Runtime(spec));
        builder.RegisterFactory<ICallAbiProvider>(new("fixture.abi." + spec.id, ExtensionPort.CallAbiProvider), _ => new Abi(spec));
    }
    private sealed record Spec(
        string id,
        string platform,
        string runtime,
        int pointerSize
    )
    {
        internal string triple => "fixture-" + id;
        internal CallAbiId abi => new("fixture.scalar." + id);
    }
    private sealed class Target(Spec spec) : INativeTargetProvider
    {
        public bool TryResolve(
            NativeTargetRequest request,
            [NotNullWhen(true)] out NativeTargetDescriptor? target
        ) {
            target = request.targetId.value == spec.id ? new(new(spec.id), spec.platform, "private-cpu", "private-abi", spec.triple,
                new(defines: ["FIXTURE_POINTER_BYTES=" + spec.pointerSize.ToString(CultureInfo.InvariantCulture)]), new(false, true), NativeByteOrder.LittleEndian) : null;
            return target is not null;
        }
    }
    private sealed class Runtime(Spec spec) : IManagedRuntimeProvider
    {
        public bool TryResolve(
            ManagedRuntimeRequest request,
            [NotNullWhen(true)] out ManagedRuntimeProfile? profile
        ) {
            profile = null;
            if (request.runtimeId.value != spec.runtime || request.target.targetId.value != spec.id) return false;
            profile = new("fixture-profile/" + spec.id, new(spec.runtime), NativeModuleMechanism.DynamicLibrary,
                [BindingImportMode.FunctionTable], false, true)
            { nativeTargetId = spec.id, nativeTriple = spec.triple, pointerSize = spec.pointerSize, byteOrder = NativeByteOrder.LittleEndian, callAbi = spec.abi };
            return true;
        }
    }
    private sealed class Abi(Spec spec) : ICallAbiProvider
    {
        public CallAbiAssessment Assess(CallAbiRequest request)
        {
            if (request.runtime.runtimeId.value != spec.runtime || request.runtime.callAbi != spec.abi)
                return new(spec.abi, NativeCallDisposition.NotApplicable, "This test provider owns another explicit scalar execution model.");
            if (request.signature.direction != NativeCallDirection.Forward || request.signature.isVariadic || !request.signature.hasPrototype)
                return new(spec.abi, NativeCallDisposition.Unsupported, "The independent fixture implements only exact forward scalar declarations.");
            var result = Value(request.signature.returnType);
            var parameters = request.signature.parameterTypes.Select(Value).ToImmutableArray();
            return new(spec.abi, NativeCallDisposition.Direct, "Synthetic scalar source mechanism; no native execution certification is asserted.",
                new(request.signature, spec.id, request.runtime.runtimeId, request.runtime.id, spec.abi, spec.pointerSize, NativeByteOrder.LittleEndian, result, parameters)
                { nativeTriple = spec.triple, runtimeProfileDigest = ManagedRuntimeProfileIdentity.Capture(request.runtime) });
            NativeCallValuePlan Value(BGCS.Core.Common.StableId id)
            {
                InteropType type = request.module.GetType(id);
                if (type.category != NativeTypeCategory.Primitive || type.primitiveKind != "Int" || type.size != 4 || type.alignment != 4)
                    throw new NotSupportedException("The test provider grants only its declared integer carrier.");
                return new(id, 4, 4, ["FixtureScalar32"]);
            }
        }
    }
    private sealed class Frontend(IHostPathService paths) : IFrontend
    {
        public ValueTask<FrontendPreparation> PrepareAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        )
            => ValueTask.FromResult(new FrontendPreparation(request, new([], []), ["Synthetic frontend source fixture has no reusable parser execution closure."]));
        public ValueTask<FrontendResult> ParseAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        ) {
            string header = request.headers.Single();
            Assert.Equal("int Echo(int value);", File.ReadAllText(header));
            BGCS.Core.Common.SourceLocation source = new(header, 1, 1);
            NativeType integer = new(new("fixture-int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
            NativeDeclaration function = new(new("fixture-echo"), "Function", "Echo", "Echo", null, source)
            { returnType = integer.id, children = [new("fixture-value")], callingConvention = "C", linkage = "C", nativeLinkage = NativeLinkageKind.External };
            NativeDeclaration parameter = new(new("fixture-value"), "Parameter", "value", "Echo::value", integer.id, source) { parent = function.id };
            int pointerSize = int.Parse(request.target.toolchain.defines.Single().Split('=')[1], CultureInfo.InvariantCulture);
            NativeModule module = new(request.target.targetId.value, request.target.triple, pointerSize,
                [integer], [function, parameter], [])
            { language = SourceLanguage.C, byteOrder = NativeByteOrder.LittleEndian };
            SourceSnapshot snapshot = new ExternalInputSnapshotBuilder().Capture([new(header, ExternalInputKind.File)], request.pathBase, paths, cancellationToken);
            return ValueTask.FromResult(new FrontendResult(module, snapshot));
        }
    }
    private sealed class Host(Fixture fixture) : IHostServicesProvider
    {
        public HostServices Create(HostServicesRequest request) => new(new Paths(fixture.root, fixture.caseSensitive),
            new("fixture-host", Environment.ProcessPath ?? throw new InvalidOperationException("The test host executable is unavailable."), true));
    }
    // Intentionally limited fixture host: every mutable input/output directory is created under
    // one observed temp root, inherits its name semantics, and is never made case-sensitive later.
    private sealed class Paths(
        string root,
        bool caseSensitive
    ) : IHostPathService
    {
        public IEqualityComparer<string> comparer { get; } = caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        public HostPathIdentity GetIdentity(string path)
        {
            string logical = Path.GetFullPath(path);
            return new(logical, PhysicalPathResolver.Resolve(logical, this), GetIdentityKey(logical));
        }
        public HostPathSemantics GetSemantics(string directory) => new(caseSensitive);
        public string GetIdentityKey(string path) => caseSensitive ? Path.GetFullPath(path) : Path.GetFullPath(path).ToUpperInvariant();
        public bool Equals(
            string left,
            string right
        ) => comparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));
        public bool IsWithin(
            string candidate,
            string parent,
            bool includeRoot = true
        ) {
            string full = Path.GetFullPath(candidate), container = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
            return includeRoot && Equals(full, container) || full.StartsWith(container + Path.DirectorySeparatorChar, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }
        public string GetRelativePath(
            string baseDirectory,
            string path
        ) => Path.GetRelativePath(baseDirectory, path);
        public HostIoFailureKind ClassifyIoFailure(Exception failure) => HostIoFailureKind.Permanent;
        public void Verify()
        {
            string name = "probe-" + Guid.NewGuid().ToString("N") + "-lower";
            string probe = Path.Combine(root, name);
            File.WriteAllText(probe, "");
            try { if (File.Exists(Path.Combine(root, name.ToUpperInvariant())) == caseSensitive) throw new IOException("Fixture directory name semantics changed."); }
            finally { File.Delete(probe); }
        }
    }
    private sealed class Fixture : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-neutral-engine-" + Guid.NewGuid().ToString("N"));
        internal bool caseSensitive { get; }
        internal Fixture()
        {
            Directory.CreateDirectory(root);
            string probe = Path.Combine(root, "probe-lower"); File.WriteAllText(probe, "");
            caseSensitive = !File.Exists(Path.Combine(root, "PROBE-LOWER")); File.Delete(probe);
            File.WriteAllText(Path.Combine(root, "native.h"), "int Echo(int value);");
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
