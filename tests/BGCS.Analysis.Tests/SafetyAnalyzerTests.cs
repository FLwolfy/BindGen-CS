using System;
using System.Collections.Immutable;
using BGCS.Analysis.Safety;
using BGCS.Core;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.Analysis.Tests;

public sealed class SafetyAnalyzerTests
{
    [Theory]
    [InlineData("integer", false)]
    [InlineData("data", true)]
    [InlineData("void-pointer", true)]
    [InlineData("record-pointer", true)]
    [InlineData("alias-data", true)]
    [InlineData("qualified-data", true)]
    [InlineData("wrapped-pointee", true)]
    [InlineData("callback", false)]
    [InlineData("alias-callback", false)]
    [InlineData("qualified-callback", false)]
    [InlineData("callback-storage", true)]
    [InlineData("misleading-spelling", true)]
    public void ActualTypeShapeSeparatesDataAddressesFromFunctionAddresses(
        string resultType,
        bool warning
    ) {
        SourceLocation location = new("include/library.h", 37, 9);
        InteropFunction function = Function(resultType) with { source = location, documentation = "Create UTF8 string" };
        InteropModule module = Module(function);
        ImmutableArray<BindingDiagnostic> diagnostics = new SafetyAnalyzer().Analyze(module);
        Assert.Equal(warning ? 1 : 0, diagnostics.Length);
        if (warning)
        {
            BindingDiagnostic diagnostic = Assert.Single(diagnostics);
            Assert.Equal(BindingDiagnosticCodes.C_OWNERSHIP, diagnostic.code);
            Assert.Equal(BindingDiagnosticSeverity.Warning, diagnostic.severity);
            Assert.False(diagnostic.isFailure);
            Assert.Equal(location, diagnostic.location);
            Assert.Contains("CreateOrRead", diagnostic.message, StringComparison.Ordinal);
            Assert.Contains("semantics.ownedResults", diagnostic.message, StringComparison.Ordinal);
            Assert.Contains("semantics.borrowedResults", diagnostic.message, StringComparison.Ordinal);
            Assert.Contains("interop plugin", diagnostic.message, StringComparison.Ordinal);
        }
        Assert.Same(function, Assert.Single(module.functions));
        Assert.Null(function.returnOwnership);
        Assert.Null(function.returnString);
    }

    [Theory]
    [InlineData(NativeOwnershipKind.Owned, EvidenceLevel.Declared)]
    [InlineData(NativeOwnershipKind.Owned, EvidenceLevel.UserAsserted)]
    [InlineData(NativeOwnershipKind.Owned, EvidenceLevel.Verified)]
    [InlineData(NativeOwnershipKind.Borrowed, EvidenceLevel.Declared)]
    [InlineData(NativeOwnershipKind.Borrowed, EvidenceLevel.UserAsserted)]
    [InlineData(NativeOwnershipKind.Borrowed, EvidenceLevel.Verified)]
    public void ExplicitEvidenceIsPreservedWithoutChangingItsStrength(
        NativeOwnershipKind kind,
        EvidenceLevel level
    ) {
        Evidence evidence = new(level, "fixture:actual-evidence-origin");
        OwnershipContract facts = new(kind, new(kind == NativeOwnershipKind.Owned ? NativeLifetimeKind.Owner : NativeLifetimeKind.Module,
            kind == NativeOwnershipKind.Owned ? new("get") : null, evidence), evidence);
        InteropFunction function = Function("alias-data") with { returnOwnership = facts };
        Assert.Empty(new SafetyAnalyzer().Analyze(Module(function)));
        OwnershipContract preserved = Assert.IsType<OwnershipContract>(function.returnOwnership);
        Assert.Same(facts, preserved);
        Assert.Same(evidence, preserved.evidence);
        Assert.Same(evidence, preserved.lifetime.evidence);
        Assert.Equal(level, facts.evidence.level);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown-kind")]
    [InlineData("undefined-kind")]
    [InlineData("unknown-evidence")]
    [InlineData("inferred-evidence")]
    [InlineData("empty-origin")]
    [InlineData("null-evidence")]
    [InlineData("unknown-lifetime")]
    [InlineData("undefined-lifetime")]
    [InlineData("unknown-lifetime-evidence")]
    [InlineData("inferred-lifetime-evidence")]
    [InlineData("empty-lifetime-origin")]
    [InlineData("null-lifetime-evidence")]
    [InlineData("null-lifetime")]
    [InlineData("missing-owner")]
    [InlineData("empty-owner-identity")]
    [InlineData("missing-registration")]
    [InlineData("missing-operation")]
    public void IncompleteOwnershipAndLifetimeProduceOneActionableWarning(string fault)
    {
        Evidence declared = new(EvidenceLevel.Declared, "source:annotation");
        OwnershipContract facts = new(NativeOwnershipKind.Owned, new(NativeLifetimeKind.Owner, new("get"), declared), declared);
        facts = fault switch
        {
            "unknown-kind" => facts with { kind = NativeOwnershipKind.Unknown },
            "undefined-kind" => facts with { kind = (NativeOwnershipKind)999 },
            "unknown-evidence" => facts with { evidence = new(EvidenceLevel.Unknown, "unknown") },
            "inferred-evidence" => facts with { evidence = new(EvidenceLevel.Inferred, "Create-name") },
            "empty-origin" => facts with { evidence = declared with { origin = " " } },
            "null-evidence" => facts with { evidence = null! },
            "unknown-lifetime" => facts with { lifetime = facts.lifetime with { kind = NativeLifetimeKind.Unknown } },
            "undefined-lifetime" => facts with { lifetime = facts.lifetime with { kind = (NativeLifetimeKind)999 } },
            "unknown-lifetime-evidence" => facts with { lifetime = facts.lifetime with { evidence = declared with { level = EvidenceLevel.Unknown } } },
            "inferred-lifetime-evidence" => facts with { lifetime = facts.lifetime with { evidence = declared with { level = EvidenceLevel.Inferred } } },
            "empty-lifetime-origin" => facts with { lifetime = facts.lifetime with { evidence = declared with { origin = "" } } },
            "null-lifetime-evidence" => facts with { lifetime = facts.lifetime with { evidence = null! } },
            "null-lifetime" => facts with { lifetime = null! },
            "missing-owner" => facts with { lifetime = facts.lifetime with { owner = null } },
            "empty-owner-identity" => facts with { lifetime = facts.lifetime with { owner = default(StableId) } },
            "missing-registration" => facts with { lifetime = facts.lifetime with { kind = NativeLifetimeKind.Registration, owner = null } },
            "missing-operation" => facts with { lifetime = facts.lifetime with { kind = NativeLifetimeKind.Operation, owner = null } },
            _ => facts
        };
        InteropFunction function = Function("data") with { returnOwnership = fault == "missing" ? null : facts };
        BindingDiagnostic diagnostic = Assert.Single(new SafetyAnalyzer().Analyze(Module(function)));
        Assert.Equal(BindingDiagnosticCodes.C_OWNERSHIP, diagnostic.code);
        Assert.Equal(BindingDiagnosticSeverity.Warning, diagnostic.severity);
        Assert.Contains("incomplete safety facts", diagnostic.message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NativeLifetimeKind.Call)]
    [InlineData(NativeLifetimeKind.Static)]
    [InlineData(NativeLifetimeKind.Module)]
    public void ExistingLifetimeAssertionsAreReportedAsFactsWithoutInventingAPostReturnWrapper(NativeLifetimeKind lifetime)
    {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "plugin:explicit-lifetime");
        OwnershipContract facts = new(NativeOwnershipKind.Borrowed, new(lifetime, null, evidence), evidence);
        InteropFunction function = Function("data") with { returnOwnership = facts };
        Assert.Empty(new SafetyAnalyzer().Analyze(Module(function)));
        Assert.Equal(lifetime, function.returnOwnership!.lifetime.kind);
        Assert.Null(function.returnString);
    }

    [Theory]
    [InlineData("alias-missing-element")]
    [InlineData("qualified-missing-element")]
    [InlineData("alias-missing-target")]
    [InlineData("alias-cycle")]
    [InlineData("pointee-alias-cycle")]
    [InlineData("pointer-self-cycle")]
    [InlineData("pointer-chain-cycle")]
    [InlineData("pointer-missing-element")]
    [InlineData("pointer-missing-target")]
    [InlineData("undefined-shape")]
    [InlineData("empty-type-identity")]
    public void MalformedReturnedRepresentationsFailBeforeAnyPartialDiagnosticSetEscapes(string fault)
    {
        var types = Types().ToBuilder();
        InteropType broken = new(new("broken"), "broken", NativeTypeCategory.Alias, 8, 8);
        broken = fault switch
        {
            "qualified-missing-element" => broken with { category = NativeTypeCategory.Qualified },
            "alias-missing-target" => broken with { elementType = new("missing") },
            "alias-cycle" => broken with { elementType = broken.id },
            "pointee-alias-cycle" => broken with { category = NativeTypeCategory.Pointer, elementType = new("bad-alias") },
            "pointer-self-cycle" => broken with { category = NativeTypeCategory.Pointer, elementType = broken.id },
            "pointer-chain-cycle" => broken with { category = NativeTypeCategory.Pointer, elementType = new("second-pointer") },
            "pointer-missing-element" => broken with { category = NativeTypeCategory.Pointer },
            "pointer-missing-target" => broken with { category = NativeTypeCategory.Pointer, elementType = new("missing") },
            "undefined-shape" => broken with { category = (NativeTypeCategory)999 },
            "empty-type-identity" => broken with { id = default, category = NativeTypeCategory.Pointer, elementType = new("integer") },
            _ => broken
        };
        types.Add(broken);
        if (fault == "pointee-alias-cycle") types.Add(new(new("bad-alias"), "bad-alias", NativeTypeCategory.Alias, 8, 8) { elementType = new("bad-alias") });
        if (fault == "pointer-chain-cycle") types.Add(new(new("second-pointer"), "second", NativeTypeCategory.Pointer, 8, 8) { elementType = broken.id });
        InteropModule module = new("safety", "windows-x64-msvc", 8, types.ToImmutable(), [Function("data"), Function("broken") with { id = new("broken-get"), exportName = "BrokenGet", returnType = broken.id }], []);
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new SafetyAnalyzer().Analyze(module));
        Assert.Contains("BrokenGet", failure.Message, StringComparison.Ordinal);
        Assert.Contains("cyclic or incomplete", failure.Message, StringComparison.Ordinal);
        Assert.Empty(module.diagnostics);
    }

    [Fact]
    public void RecordRecursionAndRecursiveCallbackSignaturesAreValidSeparateFromPointerRepresentationCycles()
    {
        var types = Types().ToBuilder();
        types.Add(new(new("recursive-node"), "Node", NativeTypeCategory.Record, 8, 8)
        { fields = [new(new("next"), "next", new("recursive-node-pointer"), 0, null, null)] });
        types.Add(new(new("recursive-node-pointer"), "Node*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("recursive-node") });
        types.Add(new(new("recursive-callable"), "callback", NativeTypeCategory.Function, null, null)
        { returnType = new("recursive-callback"), parameterTypes = [new("recursive-node-pointer")] });
        types.Add(new(new("recursive-callback"), "callback*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("recursive-callable") });
        InteropModule module = new("safety", "windows-x64-msvc", 8, types.ToImmutable(),
            [Function("recursive-node-pointer"), Function("recursive-callback") with { id = new("callback-get"), exportName = "GetCallback" }], []);
        BindingDiagnostic diagnostic = Assert.Single(new SafetyAnalyzer().Analyze(module));
        Assert.Contains("CreateOrRead", diagnostic.message, StringComparison.Ordinal);
        Assert.DoesNotContain("GetCallback", diagnostic.message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalysisDoesNotMutateOrEchoExistingDiagnosticsOrRequireBackendState()
    {
        BindingDiagnostic existing = new(BindingDiagnosticSeverity.Warning, "Existing semantic observation", "plugin.observation");
        InteropModule module = new("safety", "custom-target-without-a-managed-runtime", 8, Types(), [Function("data")], [existing]);
        ImmutableArray<BindingDiagnostic> first = new SafetyAnalyzer().Analyze(module);
        ImmutableArray<BindingDiagnostic> second = new SafetyAnalyzer().Analyze(module);
        Assert.Equal<BindingDiagnostic>(first, second);
        Assert.Equal(BindingDiagnosticCodes.C_OWNERSHIP, Assert.Single(first).code);
        Assert.Same(existing, Assert.Single(module.diagnostics));
        Assert.Empty(new SafetyAnalyzer().Analyze(new("empty", "custom-target", 8, [], [], [])));
    }

    [Fact]
    public void NullModuleFailsAtThePublicBoundary() => Assert.Throws<ArgumentNullException>(() => new SafetyAnalyzer().Analyze(null!));

    private static InteropFunction Function(string type) => new(new("get"), "CreateOrRead", new(type), [], "C");
    private static InteropModule Module(InteropFunction function) => new("safety", "windows-x64-msvc", 8, Types(), [function], []);
    private static ImmutableArray<InteropType> Types() =>
    [
        new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true },
        new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" },
        new(new("record"), "Object", NativeTypeCategory.Record, 4, 4),
        new(new("data"), "int*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("integer"), source = new("generated/bridge.h", 1, 1) },
        new(new("void-pointer"), "void*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("void") },
        new(new("record-pointer"), "Object*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("record") },
        new(new("alias-data"), "IntegerAddress", NativeTypeCategory.Alias, 8, 8) { elementType = new("qualified-data") },
        new(new("qualified-data"), "int* const", NativeTypeCategory.Qualified, 8, 8) { elementType = new("data"), qualifier = "Const" },
        new(new("qualified-integer"), "const int", NativeTypeCategory.Qualified, 4, 4) { elementType = new("integer"), qualifier = "Const" },
        new(new("alias-integer"), "ConstInt", NativeTypeCategory.Alias, 4, 4) { elementType = new("qualified-integer") },
        new(new("wrapped-pointee"), "ConstInt*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("alias-integer") },
        new(new("callable"), "Callback", NativeTypeCategory.Function, null, null) { returnType = new("integer"), parameterTypes = [new("integer")] },
        new(new("callback"), "Callback*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("callable") },
        new(new("alias-callback"), "CallbackAlias", NativeTypeCategory.Alias, 8, 8) { elementType = new("callback") },
        new(new("qualified-callable"), "Callback const", NativeTypeCategory.Qualified, null, null) { elementType = new("callable"), qualifier = "Const" },
        new(new("qualified-callback"), "Callback const*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("qualified-callable") },
        new(new("callback-storage"), "Callback**", NativeTypeCategory.Pointer, 8, 8) { elementType = new("alias-callback") },
        new(new("misleading-spelling"), "void (*LooksLikeAFunction)(void)", NativeTypeCategory.Pointer, 8, 8) { elementType = new("integer") }
    ];
}
