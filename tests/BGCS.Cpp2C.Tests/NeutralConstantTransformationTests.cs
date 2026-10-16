using System;
using System.Collections.Immutable;
using BGCS.Analysis.Interop;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class NeutralConstantTransformationTests
{
    [Fact]
    public void SelectionAndEquivalentImmutableLiteralCopiesPreserveEstablishedNativeValues()
    {
        InteropModule baseline = Module();
        InteropStringConstant original = baseline.stringConstants[0];
        InteropStringConstant copied = original with { value = original.value with
            { codeUnits = ImmutableArray.CreateRange(original.value.codeUnits.AsSpan().ToArray()), literalTokens = ImmutableArray.CreateRange(original.value.literalTokens.AsSpan().ToArray()) } };
        new InteropModuleVerifier().VerifyPreservedAbi(baseline, Copy(baseline.constants, [copied]));
        new InteropModuleVerifier().VerifyPreservedAbi(baseline, Copy([], []));
    }

    [Fact]
    public void ScalarValueWidthIdentityAndEvidenceCannotBeRewrittenAfterNativeAnalysis()
    {
        InteropModule baseline = Module();
        InteropConstant original = baseline.constants[0];
        foreach (InteropConstant changed in new[]
        {
            original with { value = original.value with { value = "43" } },
            original with { value = original.value with { width = 8 } },
            original with { value = original.value with { evidence = new(EvidenceLevel.Verified, "invented:proof") } },
            original with { sourceDeclaration = new("other") },
            original with { name = "CHANGED" }
        }) Assert.Throws<InvalidOperationException>(() => new InteropModuleVerifier().VerifyPreservedAbi(baseline, Copy([changed], baseline.stringConstants)));
    }

    [Fact]
    public void StringUnitWidthKindTokensAndEvidenceCannotBeRewrittenAfterNativeAnalysis()
    {
        InteropModule baseline = Module();
        InteropStringConstant original = baseline.stringConstants[0];
        foreach (NativeStringConstant changed in new[]
        {
            original.value with { codeUnits = [66, 0] },
            original.value with { unitWidth = 2 },
            original.value with { kind = NativeStringLiteralKind.Ordinary },
            original.value with { literalTokens = ["u8\"B\""] },
            original.value with { evidence = new(EvidenceLevel.Verified, "invented:proof") }
        }) Assert.Throws<InvalidOperationException>(() => new InteropModuleVerifier().VerifyPreservedAbi(baseline, Copy(baseline.constants, [original with { value = changed }])));
    }

    [Fact]
    public void SourceIdentityCannotChangeItsNativeConstantKindOrBecomeDuplicated()
    {
        InteropModule baseline = Module();
        Assert.Throws<InvalidOperationException>(() => new InteropModuleVerifier().VerifyPreservedAbi(baseline,
            Copy([baseline.constants[0] with { sourceDeclaration = new("text") }], [])));
        Assert.Throws<InvalidOperationException>(() => new InteropModuleVerifier().VerifyPreservedAbi(baseline,
            Copy([], [baseline.stringConstants[0] with { sourceDeclaration = new("number") }])));
        Assert.Throws<InvalidOperationException>(() => new InteropModuleVerifier().VerifyPreservedAbi(baseline,
            Copy([baseline.constants[0], baseline.constants[0]], baseline.stringConstants)));
    }

    private static InteropModule Module() => Copy(
        [new(new("number"), "NUMBER", "NUMBER", new(NativeScalarConstantKind.Integer, "42", 4, true, Evidence), null)],
        [new(new("text"), "TEXT", "TEXT", new(NativeStringLiteralKind.Utf8, [65, 0], 1, Evidence) { literalTokens = ["u8\"A\""] }, null)]);

    private static Evidence Evidence => new(EvidenceLevel.Declared, "compiler:source-constant");
    private static InteropModule Copy(ImmutableArray<InteropConstant> scalars, ImmutableArray<InteropStringConstant> strings) =>
        new("constants", "windows-x64-msvc", 8, [], [], []) { constants = scalars, stringConstants = strings };
}
