using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using Xunit;

namespace BGCS.Tests;

public sealed class NeutralUnifiedModelParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedSourceFactsSurviveCompositionAndStrictOwnershipGatesPreservePreviousPublication(bool allWarnings)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-unified-model-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), """
                #define BGCS_LIMIT 42
                #define BGCS_LABEL "bgcs"
                typedef unsigned int BgcsId;
                typedef void (*BgcsCallback)(int value);
                typedef struct BgcsItem { int id; float values[4]; } BgcsItem;
                int bgcs_get_items(BgcsItem* output, int capacity, int* actual_count);
                char* bgcs_create_name(void);
                void bgcs_free_name(char* value);
                void* bgcs_get_context(void);
                """);
            BindingConfiguration configuration = Configuration(allWarnings);
            await using BindingHost host = Host();
            GenerationResult initial = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root), compileManaged: true);
            Assert.True(initial.success, initial.GetDiagnosticSummary());
            AssertFacts(Assert.Single(initial.targetModels));
            BindingDiagnostic warning = Assert.Single(initial.diagnostics, diagnostic => diagnostic.code == BindingDiagnosticCodes.C_OWNERSHIP);
            Assert.Equal(BindingDiagnosticSeverity.Warning, warning.severity);
            Assert.Equal(new SourceLocation("native.h", 9, 7), warning.location);
            Assert.Contains("bgcs_get_context", warning.message, StringComparison.Ordinal);
            Assert.Contains("semantics.ownedResults", warning.message, StringComparison.Ordinal);
            Assert.Contains("semantics.borrowedResults", warning.message, StringComparison.Ordinal);
            string sources = string.Join("\n", Directory.GetFiles(initial.outputRoot, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
            Assert.Contains("Utf8Codec.Decode(", sources, StringComparison.Ordinal);
            Assert.Contains("Span<", sources, StringComparison.Ordinal);
            Assert.Contains("delegate void BgcsCallback", sources, StringComparison.Ordinal);
            Assert.DoesNotContain("partial struct uint", sources, StringComparison.Ordinal);

            Dictionary<string, string> previous = Snapshot(initial.outputRoot);
            DiagnosticPolicyConfiguration policy = new()
            {
                warningsAsErrors = allWarnings,
                errorCodes = allWarnings ? [] : [BindingDiagnosticCodes.C_OWNERSHIP]
            };
            BindingConfiguration strict = configuration with { execution = configuration.execution with { diagnostics = policy } };
            GenerationResult rejected = await host.GenerateAsync(new(strict, root));
            Assert.False(rejected.success);
            if (allWarnings)
            {
                // A driver warning may stop an earlier gate before ownership analysis runs.
                HashSet<string?> originalWarningCodes = initial.diagnostics
                    .Where(diagnostic => diagnostic.severity == BindingDiagnosticSeverity.Warning).Select(diagnostic => diagnostic.code).ToHashSet();
                Assert.Contains(rejected.diagnostics, diagnostic => diagnostic.isFailure);
                Assert.DoesNotContain(rejected.diagnostics, diagnostic => diagnostic.severity == BindingDiagnosticSeverity.Warning);
                Assert.All(rejected.diagnostics.Where(diagnostic => diagnostic.isFailure), diagnostic => Assert.Contains(diagnostic.code, originalWarningCodes));
            }
            else Assert.Equal(BindingDiagnosticSeverity.Error,
                Assert.Single(rejected.diagnostics, diagnostic => diagnostic.code == BindingDiagnosticCodes.C_OWNERSHIP).severity);
            Dictionary<string, string> after = Snapshot(initial.outputRoot);
            Assert.Equal(previous.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
            foreach ((string path, string digest) in previous) Assert.Equal(digest, after[path]);

            // A new explicit semantic input is analyzed afresh even when generation caching is enabled.
            BindingConfiguration declared = strict with
            { execution = strict.execution with { diagnostics = new() { errorCodes = [BindingDiagnosticCodes.C_OWNERSHIP] } }, semantics = strict.semantics with
                { borrowedResults = [new() { function = "bgcs_get_context", lifetime = NativeLifetimeKind.Module }] } };
            GenerationResult recovered = await host.GenerateAsync(new(declared, root));
            Assert.True(recovered.success, recovered.GetDiagnosticSummary());
            Assert.DoesNotContain(recovered.diagnostics, diagnostic => diagnostic.code == BindingDiagnosticCodes.C_OWNERSHIP);
            OwnershipContract borrowed = Assert.IsType<OwnershipContract>(Assert.Single(Assert.Single(recovered.targetModels).interop.functions,
                function => function.exportName == "bgcs_get_context").returnOwnership);
            Assert.Equal(NativeOwnershipKind.Borrowed, borrowed.kind);
            Assert.Equal(NativeLifetimeKind.Module, borrowed.lifetime.kind);
            Assert.Equal(EvidenceLevel.UserAsserted, borrowed.evidence.level);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("uninitialized")]
    [InlineData("duplicate")]
    [InlineData("empty")]
    [InlineData("whitespace")]
    [InlineData("control")]
    [InlineData("too-long")]
    public void DiagnosticPromotionCodesMustBeCompleteExactIdentities(string fault)
    {
        ImmutableArray<string> codes = fault switch
        {
            "uninitialized" => default,
            "duplicate" => ["vendor.warning", "vendor.warning"],
            "empty" => [""],
            "whitespace" => ["vendor. warning"],
            "control" => ["vendor.\0warning"],
            _ => [new string('x', 257)]
        };
        BindingConfiguration configuration = Configuration(false);
        Assert.Throws<ArgumentException>(() => new GenerationRequest(configuration with
        { execution = new() { diagnostics = new() { errorCodes = codes } } }, Path.GetTempPath()));
    }

    private static void AssertFacts(TargetInspection target)
    {
        NativeType Resolve(NativeType type)
        {
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
                type = target.native.GetType(type.elementType!.Value);
            return type;
        }
        NativeType alias = Assert.Single(target.native.types, type => type.category == NativeTypeCategory.Alias && type.spelling == "BgcsId");
        Assert.Equal("UnsignedInt", Resolve(alias).primitiveKind);
        NativeType callback = Resolve(Assert.Single(target.native.types, type => type.category == NativeTypeCategory.Alias && type.spelling == "BgcsCallback"));
        Assert.Equal(NativeTypeCategory.Pointer, callback.category);
        NativeType prototype = target.native.GetType(callback.elementType!.Value);
        Assert.Equal(NativeTypeCategory.Function, prototype.category);
        Assert.Equal("Void", target.native.GetType(prototype.returnType!.Value).primitiveKind);
        Assert.Single(prototype.parameterTypes);
        var item = Assert.Single(target.interop.types, type => type.category == NativeTypeCategory.Record && type.declarationName == "BgcsItem");
        Assert.Equal(20, item.size);
        var values = Assert.Single(item.fields, field => field.name == "values");
        Assert.Equal(4, values.offset);
        Assert.Equal(4, target.interop.GetType(values.type).arrayLength);
        InteropConstant limit = Assert.Single(target.interop.constants, constant => constant.name == "BGCS_LIMIT");
        Assert.Equal("42", limit.value.value);
        Assert.Equal(NativeScalarConstantKind.Integer, limit.value.kind);
        Assert.Equal(new uint[] { 98, 103, 99, 115, 0 }, Assert.Single(target.interop.stringConstants,
            constant => constant.name == "BGCS_LABEL").value.codeUnits);
        InteropFunction items = Assert.Single(target.interop.functions, function => function.exportName == "bgcs_get_items");
        BufferContract buffer = Assert.IsType<BufferContract>(items.parameters[0].buffer);
        Assert.Equal(NativeLengthUnit.Elements, buffer.lengthUnit);
        Assert.Equal(NativeParameterDirection.Output, buffer.direction);
        Assert.Equal(items.parameters[1].id, buffer.capacityParameter);
        Assert.Equal(items.parameters[2].id, buffer.writtenCountParameter);
        Assert.Equal(false, buffer.retainsPointer);
        Assert.Equal(EvidenceLevel.UserAsserted, buffer.evidence.level);
        InteropFunction create = Assert.Single(target.interop.functions, function => function.exportName == "bgcs_create_name");
        OwnershipContract owned = Assert.IsType<OwnershipContract>(create.returnOwnership);
        Assert.Equal(NativeOwnershipKind.Owned, owned.kind);
        Assert.Equal(Assert.Single(target.interop.functions, function => function.exportName == "bgcs_free_name").id, owned.releaseFunction);
        StringContract text = Assert.IsType<StringContract>(create.returnString);
        Assert.Equal(NativeTextEncoding.Utf8, text.encoding);
        Assert.Equal(true, text.zeroTerminated);
        Assert.Equal(EvidenceLevel.UserAsserted, text.evidence.level);
    }

    private static BindingConfiguration Configuration(bool singleFile) => new()
    {
        schemaVersion = 1, library = new() { id = "unified-model", nativeName = "unified_fixture" },
        source = new() { language = SourceLanguage.C, headers = ["native.h"], includeTransitivelyReferencedHeaders = false },
        api = new() { @namespace = "Generated.Unified", name = "UnifiedApi" },
        semantics = new()
        {
            bufferParameters = [new() { function = "bgcs_get_items", parameter = "output", capacityParameter = "capacity",
                writtenCountParameter = "actual_count", lengthUnit = NativeLengthUnit.Elements, direction = NativeParameterDirection.Output,
                lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = true }],
            ownedResults = [new() { function = "bgcs_create_name", releaseFunction = "bgcs_free_name", releaseDomain = "native-heap", releaseThreadDomain = "any" }],
            stringResults = [new() { function = "bgcs_create_name", encoding = NativeTextEncoding.Utf8, zeroTerminated = true, allowsNull = true }]
        },
        output = new() { layout = singleFile ? "singleFile" : "split", singleFileName = "Bindings.cs" },
         cache = new() { enabled = true },
        execution = new() { diagnostics = new() { errorCodes = ["vendor.unrelated-warning"] } }
    };

    private static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private static BindingHost Host()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(Path.Combine(root!.FullName,
            "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0")).Build();
    }
}
