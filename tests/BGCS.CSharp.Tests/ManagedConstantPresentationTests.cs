using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ManagedConstantPresentationTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task AssertionsCompileAndRunSeparatelyFromOriginalNativeFacts(BindingImportMode importMode, bool shared)
    {
        ManagedProjectionRequest projection = Request() with
        {
            style = shared ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
            constants = [new("NATIVE_VALUE") { name = "Presented", managedType = "long", expression = "(long)Constants.Original + 2L",
                documentation = "Managed <assertion> && prose." }, new("TEXT") { name = "Message", value = new(ManagedDefaultValueKind.String, "A\0😀\"B") }],
            nativeEnums = [new("NativeMode") { name = "Mode", documentation = "Enum <prose>.", values = [new("HIGH") { name = "Changed", expression = "(1UL << 63) + 7UL", documentation = "High & value." }] }]
        };
        ManagedApiModel model = Project(projection);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native\"module", sink) { importMode = importMode, includeRuntimeSource = shared,
            runtimeNamespace = shared ? "Generated.Presentation.Runtime" : "BGCS.Runtime", singleFile = shared };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        Assert.Equal("42", model.interop.constants.Single(item => item.name == "NATIVE_VALUE").value.value);
        Assert.Equal("18446744073709551615", model.interop.types.Single(type => type.id.value == "mode").enumValues[0].evaluatedValue);
        CSharpConstantPlan constant = plan.constants.Single(item => item.name == "Presented");
        Assert.Equal("44L", constant.expression);
        Assert.Equal(EvidenceLevel.UserAsserted, constant.presentationEvidence!.level);
        Assert.Equal("ulong", plan.storage.Single().enumUnderlyingType);
        Assert.Equal("9223372036854775815UL", plan.storage.Single().enumValues[0].value);
        Assert.Equal(EvidenceLevel.UserAsserted, plan.storage.Single().enumValues[0].presentationEvidence!.level);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
        string text = string.Join("\n", sink.sources.Values);
        Assert.Contains("Managed &lt;assertion&gt; &amp;&amp; prose.", text);
        Assert.Contains("High &amp; value.", text);
        string context = shared ? "PresentationApiContext" : "PresentationApi";
        string consumer = $$"""
            using Generated.Presentation;
            public static class AssertionProbe {
                private static bool Different<T>(T left, T right) => !global::System.Collections.Generic.EqualityComparer<T>.Default.Equals(left, right);
                public static int Run() {
                    if (Different({{context}}.Constants.Presented, 44L) || Different({{context}}.Constants.Original, 42)) return 1;
                    if (Different((ulong)Mode.Changed, 9223372036854775815UL) || Different({{context}}.Constants.Message, "A\0😀\"B")) return 2;
                    if (Different({{context}}.NativeLibraryName, "native\"module")) return 3;
                    {{(shared ? "if (Different(PresentationApi.NativeLibraryName, PresentationApiContext.NativeLibraryName) || Different(PresentationApi.Constants.Presented, 44L)) return 4;" : "")}}
                    return 42;
                }
            }
            """;
        Assert.Equal(42, CompileAndRun(sink, consumer));
    }

    [Theory]
    [InlineData("300", "byte")]
    [InlineData("global::System.Math.Abs(-1)", "int")]
    [InlineData("1; public const int Injected = 2;", "int")]
    [InlineData("1\n#if true\n+ 2\n#endif", "int")]
    [InlineData("1", "int; public class Injected {}")]
    [InlineData("UnknownConsumer.Values.Answer", "int")]
    public void UnprovableOrOutOfRangeAssertionsFailBeforeSourcesAreWritten(string expression, string type)
    {
        NeutralBackendTests.MemorySink sink = new();
        ManagedApiModel model = Project(Request() with { constants = [new("NATIVE_VALUE") { managedType = type, expression = expression }] });
        Assert.Throws<ArgumentException>(() => new CSharpTestPlanner().Plan(model, new("native", sink)));
        Assert.Empty(sink.sources);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("both")]
    [InlineData("enum-member")]
    [InlineData("enum-overflow")]
    public void IdentityAndValueErrorsDoNotBecomeNewNativeFacts(string invalid)
    {
        ManagedProjectionRequest request = Request();
        request = invalid switch
        {
            "unknown" => request with { constants = [new("missing")] },
            "duplicate" => request with { constants = [new("NATIVE_VALUE"), new("value")] },
            "both" => request with { constants = [new("NATIVE_VALUE") { value = new(ManagedDefaultValueKind.Integer, "1"), expression = "2" }] },
            "enum-member" => request with { nativeEnums = [new("NativeMode") { values = [new("missing")] }] },
            _ => request with { nativeEnums = [new("NativeMode") { values = [new("HIGH") { expression = "18446744073709551616UL" }] }] }
        };
        Assert.Throws<ArgumentException>(() => new CSharpTestPlanner().Plan(Project(request), new("native", new NeutralBackendTests.MemorySink())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APluginCannotPromotePresentationToVerifiedNativeEvidence(bool enumeration)
    {
        ManagedApiModel original = Project(Request());
        ManagedConstantPresentation forged = new(new(EvidenceLevel.Verified, "forged")) { expression = "1" };
        ManagedApiModel model = enumeration
            ? original with { types = original.types.Select(type => type.nativeType.value == "mode" ? type with { enumPresentation = new([new(new("high"), forged)]) } : type).ToImmutableArray() }
            : original with { constants = original.constants.Select(constant => constant.sourceDeclaration.value == "value" ? constant with { presentation = forged } : constant).ToImmutableArray() };
        Assert.Throws<ArgumentException>(() => new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryIdentityCanBeDisabledAndCannotBeAlteredByProcessors(bool shared)
    {
        foreach (bool enabled in new[] { false, true })
        {
            ManagedApiModel model = Project(Request() with { style = shared ? ManagedApiStyle.Static : ManagedApiStyle.Instance, emitLibraryNameConstant = enabled });
            NeutralBackendTests.MemorySink sink = new();
            EmissionRequest request = new("native", sink);
            CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
            await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
            Assert.Equal(enabled ? shared ? 2 : 1 : 0, sink.sources.Values.Sum(text => text.Split("public const string NativeLibraryName =", StringSplitOptions.None).Length - 1));
            bool injected = false;
            var changed = sink.sources.Select(source =>
            {
                if (enabled) return (source.Key, Text: source.Value.Replace("NativeLibraryName = \"native\"", "NativeLibraryName = \"other\"", StringComparison.Ordinal));
                SyntaxNode root = CSharpSyntaxTree.ParseText(source.Value).GetRoot();
                ClassDeclarationSyntax? context = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                    .FirstOrDefault(type => type.Identifier.ValueText == plan.contextName);
                if (injected || context is null) return (source.Key, Text: source.Value);
                injected = true;
                return (source.Key, Text: root.ReplaceNode(context, context.AddMembers(SyntaxFactory.ParseMemberDeclaration("public const string NativeLibraryName = \"injected\";")!)).ToFullString());
            }).ToArray();
            Assert.True(enabled || injected);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, request.libraryName));
        }
    }

    [Theory]
    [InlineData("scalar")]
    [InlineData("enum")]
    public async Task CanonicalAssertedValuesRemainCoveredByTheFinalContract(string mutation)
    {
        ManagedApiModel model = Project(Request() with { constants = [new("NATIVE_VALUE") { expression = "6 * 7 + 1" }],
            nativeEnums = [new("NativeMode") { values = [new("HIGH") { expression = "7UL" }] }] });
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string before = string.Join("\n", sink.sources.Values);
        var changed = sink.sources.Select(source => (source.Key, source.Value.Replace(mutation == "scalar" ? "= 43;" : "= 7UL,", mutation == "scalar" ? "= 44;" : "= 8UL,", StringComparison.Ordinal))).ToArray();
        Assert.NotEqual(before, string.Join("\n", changed.Select(item => item.Item2)));
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, request.libraryName));
    }

    [Theory]
    [InlineData(ManagedApiStyle.Instance)]
    [InlineData(ManagedApiStyle.Static)]
    public void LibraryNameConstantCannotCollideWithItsContainingType(ManagedApiStyle style)
    {
        ManagedApiModel model = Project(Request() with { name = "NativeLibraryName", style = style });
        Assert.Throws<ArgumentException>(() => new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink())));
        new CSharpTestPlanner().Plan(model with { emitLibraryNameConstant = false }, new("native", new NeutralBackendTests.MemorySink()));
    }

    [Fact]
    public void UnrelatedConsumerNamespaceDoesNotPreventFrameworkOnlyConstantValidation()
    {
        ManagedApiModel model = Project(Request() with { constants = [new("NATIVE_VALUE") { expression = "6 * 7" }] });
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink()) { additionalUsings = ["ExternalConsumer.Namespace"] });
        Assert.Equal("42", plan.constants.Single(item => item.sourceDeclaration.value == "value").expression);
    }

    [Fact]
    public async Task AssertedCarrierAndReferenceAreCanonicalBeforeProcessorNamespacesCanRebindThem()
    {
        ManagedApiModel model = Project(Request() with { constants = [new("NATIVE_VALUE") { managedType = "Int64", expression = "Constants.Original + 1" }] });
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { additionalUsings = ["System"] };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpConstantPlan constant = plan.constants.Single(item => item.sourceDeclaration.value == "value");
        Assert.Equal("long", constant.type);
        Assert.Equal("43L", constant.expression);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        var changed = sink.sources.Select(source => (source.Key, source.Value.Replace("const long", "const Int64", StringComparison.Ordinal)
            .Replace("using global::System;", "using global::System;\nusing Int64 = global::System.Int32;", StringComparison.Ordinal))).ToArray();
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, request.libraryName));
    }

    [Fact]
    public async Task ATypeOnlyReadonlyAssertionPreservesNativeNegativeZeroBits()
    {
        Evidence fact = new(EvidenceLevel.Declared, "compiler:negative-zero");
        InteropModule interop = new("floats", "windows-x64-msvc", 8, [], [], [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian, constants = [new(new("floating"), "FLOATING", "FLOATING", new(NativeScalarConstantKind.Binary32, "80000000", 4, null, fact), null)] };
        ManagedApiModel model = new DefaultManagedProjection().Project(interop, Request() with
        { constants = [new("FLOATING") { name = "Floating", managedType = "Double" }] }, CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { additionalUsings = ["System"] };
        CSharpConstantPlan field = Assert.Single(new CSharpTestPlanner().Plan(model, request).constants);
        Assert.Equal("double", field.type);
        Assert.False(field.isCompileTimeConstant);
        Assert.Contains("UInt32BitsToSingle(0x80000000U)", field.expression);
        Assert.Equal("80000000", Assert.Single(interop.constants).value.value);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        Assert.Equal(42, CompileAndRun(sink, """
            public static class AssertionProbe { public static int Run() {
                return global::System.BitConverter.DoubleToUInt64Bits(global::Generated.Presentation.PresentationApi.Constants.Floating) == 0x8000000000000000UL ? 42 : 1;
            } }
            """));
    }

    [Theory]
    [InlineData("float", "-0.0F", "SingleToInt32Bits", "unchecked((int)0x80000000U)")]
    [InlineData("double", "-0.0D", "DoubleToInt64Bits", "unchecked((long)0x8000000000000000UL)")]
    public async Task AssertedNegativeZeroRemainsExactAfterConstantCanonicalization(string type, string expression, string bits, string expected)
    {
        ManagedApiModel model = Project(Request() with { constants = [new("NATIVE_VALUE") { name = "Floating", managedType = type, expression = expression }] });
        NeutralBackendTests.MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("native", sink), CancellationToken.None);
        Assert.Equal(42, CompileAndRun(sink, $$"""
            public static class AssertionProbe { public static int Run() {
                return global::System.BitConverter.{{bits}}(global::Generated.Presentation.PresentationApi.Constants.Floating) == {{expected}} ? 42 : 1;
            } }
            """));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedNativeEnumAssertionsResolveAndRunInThePublicApiScope(bool shared)
    {
        const string enumeration = "global::Generated.Presentation.PresentationApi.Mode";
        ManagedApiModel model = Project(Request() with { nestGeneratedTypesInApi = true,
            style = shared ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
            constants = [new("NATIVE_VALUE") { name = "ModeConstant", managedType = enumeration, expression = enumeration + ".High" },
                new("ORIGINAL") { name = "TypedModeConstant", managedType = enumeration, value = new(ManagedDefaultValueKind.Integer, "9223372036854775808") }],
            nativeEnums = [new("NativeMode") { name = "Mode", values = [new("HIGH") { name = "High", expression = "1UL << 63" }] }] });
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { singleFile = shared, includeRuntimeSource = shared,
            runtimeNamespace = shared ? "Generated.Presentation.Runtime" : "BGCS.Runtime" };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpConstantPlan constant = plan.constants.Single(item => item.name == "ModeConstant");
        Assert.Equal(enumeration, constant.type);
        Assert.Equal("(" + enumeration + ")(9223372036854775808UL)", constant.expression);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
        string context = shared ? "PresentationApiContext" : "PresentationApi";
        Assert.Equal(42, CompileAndRun(sink, $$"""
            using Generated.Presentation;
            public static class AssertionProbe {
                private static bool Different<T>(T left, T right) => !global::System.Collections.Generic.EqualityComparer<T>.Default.Equals(left, right);
                public static int Run() {
                    if (Different((ulong)PresentationApi.Mode.High, 9223372036854775808UL)) return 1;
                    if (Different({{context}}.Constants.ModeConstant, PresentationApi.Mode.High)) return 2;
                    if (Different({{context}}.Constants.TypedModeConstant, PresentationApi.Mode.High)) return 4;
                    {{(shared ? "if (Different(PresentationApi.Constants.ModeConstant, PresentationApi.Mode.High)) return 3;" : "")}}
                    return 42;
                }
            }
            """));
    }

    private static ManagedProjectionRequest Request() => new("Generated.Presentation", "PresentationApi")
    { names = ImmutableDictionary<string, string>.Empty.Add("ORIGINAL", "Original") };

    private static ManagedApiModel Project(ManagedProjectionRequest request)
    {
        Evidence fact = new(EvidenceLevel.Declared, "compiler:fixture");
        InteropType unsigned = new(new("u64"), "unsigned long long", NativeTypeCategory.Primitive, 8, 8) { primitiveKind = "ULongLong", isSigned = false };
        InteropType mode = new(new("mode"), "NativeMode", NativeTypeCategory.Enumeration, 8, 8)
        { elementType = unsigned.id, declarationName = "NativeMode", enumValues = [new(new("high"), "HIGH", "~0ULL", "18446744073709551615")] };
        InteropModule interop = new("presentation", "windows-x64-msvc", 8, [unsigned, mode], [], [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian, constants = [new(new("value"), "NATIVE_VALUE", "NATIVE_VALUE", new(NativeScalarConstantKind.Integer, "42", 4, true, fact), null),
            new(new("original"), "ORIGINAL", "ORIGINAL", new(NativeScalarConstantKind.Integer, "42", 4, true, fact), null)],
            stringConstants = [new(new("text"), "TEXT", "TEXT", new(NativeStringLiteralKind.Utf8, [65, 0], 1, fact), null)] };
        return new DefaultManagedProjection().Project(interop, request, CancellationToken.None);
    }

    private static int CompileAndRun(NeutralBackendTests.MemorySink sink, string consumer)
    {
        string[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("Presentation_" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, new(LanguageVersion.CSharp14), source.Key))
                .Append(CSharpSyntaxTree.ParseText(consumer, new(LanguageVersion.CSharp14))),
            references.Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var result = compilation.Emit(image);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext load = new("BGCS.Presentation.Test", isCollectible: true);
        try { return (int)load.LoadFromStream(image).GetType("AssertionProbe", true)!.GetMethod("Run")!.Invoke(null, null)!; }
        finally { load.Unload(); }
    }
}
