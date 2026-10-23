using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Safety;
using BGCS.Core;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.Analysis.Tests;

public sealed class CallbackSafetyAnalyzerTests
{
    [Theory]
    [InlineData("callback", true)]
    [InlineData("alias", true)]
    [InlineData("qualified", true)]
    [InlineData("wrapped-pointee", true)]
    [InlineData("void-pointer", false)]
    [InlineData("data", false)]
    [InlineData("callback-storage", false)]
    [InlineData("misleading-spelling", false)]
    [InlineData("integer", false)]
    public void OnlyAnExactFunctionPointerParameterRequiresCallbackLifetimeAndThreadFacts(
        string type,
        bool callback
    ) {
        InteropModule module = Module(type);
        var diagnostics = new SafetyAnalyzer().Analyze(module);
        Assert.Equal(callback ? 2 : 0, diagnostics.Length);
        if (!callback) return;
        Assert.Equal(new[] { BindingDiagnosticCodes.C_CALLBACKLIFETIME, BindingDiagnosticCodes.C_CALLBACKTHREADING }, diagnostics.Select(diagnostic => diagnostic.code));
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(BindingDiagnosticSeverity.Warning, diagnostic.severity);
            Assert.Equal(new SourceLocation("api.h", 5, 19), diagnostic.location);
            Assert.Contains("Register:callback", diagnostic.message, StringComparison.Ordinal);
            Assert.Contains("semantics.callbackParameters", diagnostic.message, StringComparison.Ordinal);
        });
        Assert.Null(Assert.Single(Assert.Single(module.functions).parameters).callback);
    }

    [Theory]
    [InlineData("lifetime", true, false)]
    [InlineData("undefined-lifetime", true, false)]
    [InlineData("retained", true, false)]
    [InlineData("lifetime-evidence", true, false)]
    [InlineData("thread", false, true)]
    [InlineData("undefined-thread", false, true)]
    [InlineData("evidence", true, true)]
    public void IndependentUnknownFieldsReportOnlyTheirMissingFacts(
        string fault,
        bool lifetime,
        bool threading
    ) {
        CallbackContract known = Contract(EvidenceLevel.UserAsserted);
        CallbackContract changed = fault switch
        {
            "lifetime" => known with { lifetime = known.lifetime with { kind = NativeLifetimeKind.Unknown } },
            "undefined-lifetime" => known with { lifetime = known.lifetime with { kind = (NativeLifetimeKind)999 } },
            "retained" => known with { retained = null },
            "lifetime-evidence" => known with { lifetime = known.lifetime with { evidence = new(EvidenceLevel.Inferred, "guess") } },
            "thread" => known with { threading = NativeCallbackThreading.Unknown },
            "undefined-thread" => known with { threading = (NativeCallbackThreading)999 },
            _ => known with { evidence = new(EvidenceLevel.Unknown, "unknown") }
        };
        var diagnostics = new SafetyAnalyzer().Analyze(Module("qualified", changed));
        Assert.Equal(lifetime, diagnostics.Any(diagnostic => diagnostic.code == BindingDiagnosticCodes.C_CALLBACKLIFETIME));
        Assert.Equal(threading, diagnostics.Any(diagnostic => diagnostic.code == BindingDiagnosticCodes.C_CALLBACKTHREADING));
        Assert.Equal((lifetime ? 1 : 0) + (threading ? 1 : 0), diagnostics.Length);
    }

    [Theory]
    [InlineData(EvidenceLevel.Declared)]
    [InlineData(EvidenceLevel.UserAsserted)]
    [InlineData(EvidenceLevel.Verified)]
    public void ExplicitContractsRemainUnchangedWithoutUpgradingTheirEvidence(EvidenceLevel level)
    {
        CallbackContract contract = Contract(level);
        InteropModule module = Module("qualified", contract);
        Assert.Empty(new SafetyAnalyzer().Analyze(module));
        Assert.Same(contract, Assert.Single(Assert.Single(module.functions).parameters).callback);
        Assert.Equal(level, contract.evidence.level);
        Assert.Equal(level, contract.lifetime.evidence.level);
    }

    [Theory]
    [InlineData("alias-cycle")]
    [InlineData("missing-element")]
    [InlineData("missing-type")]
    public void BrokenCallbackTypeRepresentationsFailBeforeAnyPartialWarningSetEscapes(string fault)
    {
        InteropModule original = Module("callback");
        var types = original.types;
        types = fault switch
        {
            "alias-cycle" => types.Add(new(new("broken"), "Cycle", NativeTypeCategory.Alias, 8, 8) { elementType = new("broken") }),
            "missing-element" => types.Add(new(new("broken"), "Broken*", NativeTypeCategory.Pointer, 8, 8)),
            _ => types.Add(new(new("broken"), "Unknown*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("absent") })
        };
        InteropFunction function = original.functions[0] with { parameters = [original.functions[0].parameters[0] with { type = new("broken") }] };
        Assert.Throws<InvalidOperationException>(() => new SafetyAnalyzer().Analyze(new(original.libraryId, original.targetId, 8, types, [function], [])));
    }

    [Fact]
    public void ExistingExactWarningsAreNotDuplicatedAndParameterLocationsFallBackToTheCallable()
    {
        InteropModule original = Module("qualified");
        InteropFunction function = original.functions[0] with { parameters = [original.functions[0].parameters[0] with { source = null }] };
        InteropModule module = new(original.libraryId, original.targetId, 8, original.types, [function], []);
        var first = new SafetyAnalyzer().Analyze(module);
        Assert.All(first, warning => Assert.Equal(function.source, warning.location));
        Assert.Empty(new SafetyAnalyzer().Analyze(new(module.libraryId, module.targetId, 8, module.types, module.functions, first)));
    }

    private static CallbackContract Contract(EvidenceLevel level)
    {
        Evidence evidence = new(level, "explicit:native-annotation");
        return new(new("signature"), new(NativeLifetimeKind.Call, null, evidence), evidence)
        { retained = false, threading = NativeCallbackThreading.CallerThread };
    }
    private static InteropModule Module(
        string type,
        CallbackContract? contract = null
    ) {
        ImmutableArray<InteropType> types =
        [
            new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true },
            new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" },
            new(new("signature"), "void(int)", NativeTypeCategory.Function, null, null) { returnType = new("void"), parameterTypes = [new("integer")] },
            new(new("callback"), "Handler*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("signature") },
            new(new("alias"), "Handler", NativeTypeCategory.Alias, 8, 8) { elementType = new("callback") },
            new(new("qualified"), "Handler const", NativeTypeCategory.Qualified, 8, 8) { elementType = new("alias"), qualifier = "Const" },
            new(new("qualified-signature"), "QualifiedFunction", NativeTypeCategory.Alias, null, null) { elementType = new("signature") },
            new(new("wrapped-pointee"), "QualifiedFunction*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("qualified-signature") },
            new(new("void-pointer"), "void*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("void") },
            new(new("data"), "int*", NativeTypeCategory.Pointer, 8, 8) { elementType = new("integer") },
            new(new("callback-storage"), "Handler**", NativeTypeCategory.Pointer, 8, 8) { elementType = new("callback") },
            new(new("misleading-spelling"), "Callback", NativeTypeCategory.Pointer, 8, 8) { elementType = new("void") }
        ];
        InteropParameter parameter = new(new("parameter"), "callback", new(type)) { callback = contract, source = new("api.h", 5, 19) };
        InteropFunction function = new(new("register"), "Register", new("void"), [parameter], "C") { source = new("api.h", 5, 1) };
        return new("callback-safety", "custom-target", 8, types, [function], []);
    }
}
