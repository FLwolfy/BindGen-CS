using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.CSharp.Emission;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class NativeMetadataTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task CanonicalCallsRetainNativeFactsAcrossImportModesAndRuntimeLayouts(
        BindingImportMode mode,
        bool sourceRuntime
    ) {
        ManagedApiModel model = Model(true, sourceRuntime);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = Request(mode, sourceRuntime, sink);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
        MethodDeclarationSyntax[] methods = Roots(sink).SelectMany(root => root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(method => method.AttributeLists.SelectMany(list => list.Attributes).Any(IsNative)).ToArray();
        Assert.Equal(mode == BindingImportMode.FunctionTable ? 2 : 4, methods.Length);
        MethodDeclarationSyntax raw = Assert.Single(methods, method => method.Identifier.ValueText == "ManagedAdd");
        Assert.Equal("native::operator\"\"_tag", Value(raw.AttributeLists.Single(list => list.Target is null && list.Attributes.Any(IsNative)).Attributes.Single()));
        Assert.Equal("int", Value(raw.AttributeLists.Single(list => list.Target?.Identifier.ValueText == "return").Attributes.Single()));
        Assert.Equal("renamedLeft", raw.ParameterList.Parameters[0].Identifier.ValueText);
        Assert.Equal(new[] { "left", "int" }, raw.ParameterList.Parameters[0].AttributeLists.SelectMany(list => list.Attributes).Select(Value));
        Assert.Equal(new[] { "right", "int" }, raw.ParameterList.Parameters[1].AttributeLists.SelectMany(list => list.Attributes).Select(Value));
        Assert.DoesNotContain(methods, method => method.Parent is ClassDeclarationSyntax owner && owner.Identifier.ValueText == plan.contextName);
        if (sourceRuntime) Assert.Contains(sink.sources.Values, source => source.Contains("class NativeNameAttribute", StringComparison.Ordinal));
        Assert.Equal("add", model.interop.functions[0].exportName);
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task DefaultOptOutEmitsNoNativeAnnotationsOrSourceFeature(BindingImportMode mode)
    {
        ManagedApiModel model = Model(false, true);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = Request(mode, true, sink);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        Assert.DoesNotContain(Roots(sink).SelectMany(root => root.DescendantNodes().OfType<AttributeSyntax>()), IsNative);
        Assert.DoesNotContain(sink.sources.Values, source => source.Contains("class NativeNameAttribute", StringComparison.Ordinal));
        Assert.False(new ManagedProjectionRequest("Example", "Api").emitNativeMetadata);
    }

    [Theory]
    [InlineData("function")]
    [InlineData("return-type")]
    [InlineData("parameter-name")]
    [InlineData("parameter-type")]
    [InlineData("target")]
    [InlineData("duplicate")]
    [InlineData("foreign-attribute")]
    public async Task FinalGuardRejectsEveryChangedSourceAnnotationBeforeRemovingAnything(string mutation)
    {
        ManagedApiModel model = Model(true, false);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = Request(BindingImportMode.FunctionTable, false, sink);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        string key = Assert.Single(sink.sources.Keys, path => path.EndsWith(".Raw.g.cs", StringComparison.Ordinal));
        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(sink.sources[key]).GetCompilationUnitRoot();
        MethodDeclarationSyntax method = Assert.Single(root.DescendantNodes().OfType<MethodDeclarationSyntax>(), candidate => candidate.Identifier.ValueText == "ManagedAdd");
        MethodDeclarationSyntax changed = method;
        if (mutation is "function" or "return-type" or "foreign-attribute" or "target" or "duplicate")
        {
            AttributeListSyntax list = method.AttributeLists[mutation == "return-type" || mutation == "target" ? 1 : 0];
            AttributeListSyntax replacement = mutation == "target" ? list.WithTarget(null)
                : mutation == "foreign-attribute" ? list.ReplaceNode(list.Attributes[0].Name, SyntaxFactory.ParseName("global::Foreign.NativeNameAttribute"))
                : list.ReplaceNode(list.Attributes[0].ArgumentList!.Arguments[1].Expression, SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("forged")));
            changed = mutation == "duplicate" ? method.AddAttributeLists(list) : method.ReplaceNode(list, replacement);
        }
        else
        {
            ParameterSyntax parameter = method.ParameterList.Parameters[0];
            AttributeSyntax attribute = parameter.AttributeLists[mutation == "parameter-name" ? 0 : 1].Attributes[0];
            changed = method.ReplaceNode(attribute.ArgumentList!.Arguments[1].Expression,
                SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("forged")));
        }
        sink.sources[key] = root.ReplaceNode(method, changed).ToFullString();
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName));
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task IndexedMetadataMatchesShuffledExportsAndStillRejectsTheLastChangedAnnotation(BindingImportMode mode)
    {
        ManagedApiModel original = NeutralBackendTests.Model();
        InteropType integer = original.interop.types[0];
        var functions = Enumerable.Range(0, 257).Select(index => new InteropFunction(new("metadata-" + index), "native_operation_" + index,
            integer.id, [new(new("argument-" + index), "value", integer.id)], "C")
        { sourceQualifiedName = "native::operation_" + index }).ToImmutableArray();
        InteropModule module = new(original.interop.libraryId, original.interop.targetId, original.interop.pointerSize,
            original.interop.types, functions, [])
        { byteOrder = original.interop.byteOrder };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.MetadataIndex", "NativeApi")
        { emitNativeMetadata = true }, CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = Request(mode, false, sink);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        InteropModule reordered = new(module.libraryId, module.targetId, module.pointerSize, module.types,
            module.functions.Reverse().ToImmutableArray(), [])
        { byteOrder = module.byteOrder };
        plan = plan with { api = model with { interop = reordered } };
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);

        string key = Assert.Single(sink.sources.Keys, path => path.EndsWith(".Raw.g.cs", StringComparison.Ordinal));
        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(sink.sources[key]).GetCompilationUnitRoot();
        MethodDeclarationSyntax[] raw = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => method.AttributeLists.SelectMany(list => list.Attributes).Any(IsNative)).ToArray();
        Assert.Equal(functions.Length, raw.Length);
        foreach (CSharpMethodPlan operation in plan.methods)
        {
            MethodDeclarationSyntax actual = Assert.Single(raw, method => method.Identifier.ValueText == operation.name.TrimStart('@'));
            Assert.Equal(functions.Single(function => function.id == operation.nativeExport).sourceQualifiedName,
                Value(actual.AttributeLists.Single(list => list.Target is null && list.Attributes.Any(IsNative)).Attributes.Single()));
        }
        MethodDeclarationSyntax last = Assert.Single(raw, method => method.Identifier.ValueText == plan.methods[^1].name.TrimStart('@'));
        AttributeSyntax annotation = last.AttributeLists.Single(list => list.Target is null && list.Attributes.Any(IsNative)).Attributes.Single();
        sink.sources[key] = root.ReplaceNode(annotation.ArgumentList!.Arguments[1].Expression,
            SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("different-native-operation"))).ToFullString();
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
            sink.sources.Select(source => (source.Key, source.Value)), request.libraryName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexedMetadataDoesNotAcceptMissingOrDuplicatedNativeExportIdentities(bool duplicate)
    {
        ManagedApiModel model = Model(true, false);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = Request(BindingImportMode.FunctionTable, false, sink);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        InteropModule module = model.interop;
        if (duplicate)
        {
            Assert.Throws<ArgumentException>(() => new InteropModule(module.libraryId, module.targetId, module.pointerSize, module.types,
                module.functions.Add(module.functions[0]), module.diagnostics));
            new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
            return;
        }
        InteropModule changed = new(module.libraryId, module.targetId, module.pointerSize, module.types,
            module.functions.RemoveAt(0), module.diagnostics)
        { byteOrder = module.byteOrder };
        plan = plan with { api = model with { interop = changed } };
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
            sink.sources.Select(source => (source.Key, source.Value)), request.libraryName));
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task MetadataIndexesPreserveTheIndependentUninitializedPhysicalPlanRejection(BindingImportMode mode)
    {
        ManagedApiModel model = Model(true, false);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = Request(mode, false, sink);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request) with { physicalImports = default };
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
            sink.sources.Select(source => (source.Key, source.Value)), request.libraryName));
        Assert.Contains("physical native import carriers", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageAndClosedSourceAttributesCompileWithWarningsAsErrorsAndHaveTypedFacts(bool sourceRuntime)
    {
        ManagedApiModel model = Model(true, sourceRuntime);
        NeutralBackendTests.MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, Request(BindingImportMode.DllImport, sourceRuntime, sink), CancellationToken.None);
        string[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("Metadata_" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, new(LanguageVersion.CSharp14, documentationMode: DocumentationMode.Diagnose), source.Key)),
            references.Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var result = compilation.Emit(image);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        string context = sourceRuntime ? "NativeApiContext" : "NativeApi";
        INamedTypeSymbol raw = compilation.GetTypeByMetadataName("Generated.Example." + context + "+RawApi")!;
        IMethodSymbol method = Assert.Single(raw.GetMembers("ManagedAdd").OfType<IMethodSymbol>());
        Assert.Equal("native::operator\"\"_tag", Assert.Single(method.GetAttributes()).ConstructorArguments[1].Value);
        Assert.Equal("int", Assert.Single(method.GetReturnTypeAttributes()).ConstructorArguments[1].Value);
        Assert.Equal(new object?[] { "left", "int" }, method.Parameters[0].GetAttributes().Select(attribute => attribute.ConstructorArguments[1].Value));
    }

    [Fact]
    public void NativeNameSourceFeatureIsClosedAndPreservesRuntimeAttributeIdentity()
    {
        var sources = new RuntimeSourceCatalog().GetSources(["metadata.native-names"], "Generated.Attributes");
        Assert.Single(sources);
        Assert.Contains("enum NativeNameType", sources[0].Text, StringComparison.Ordinal);
        Assert.Contains("class NativeNameAttribute", sources[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceLocationAttribute", sources[0].Text, StringComparison.Ordinal);
        BGCS.Runtime.NativeNameAttribute attribute = new(BGCS.Runtime.NativeNameType.Param, "native_argument");
        Assert.Equal(BGCS.Runtime.NativeNameType.Param, attribute.type);
        Assert.Equal("native_argument", attribute.name);
    }

    [Theory]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task BrowserPhysicalExternRetainsNativeNamesWhileTypedForwarderHasNoImportMetadata(BindingImportMode mode)
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType empty = new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" };
        InteropType callback = new(new("callback"), "int (int)", NativeTypeCategory.Function, null, null)
        { returnType = integer.id, parameterTypes = [integer.id], callingConvention = "C" };
        InteropType pointer = new(new("callback-pointer"), "int (*)(int)", NativeTypeCategory.Pointer, 4, 4) { elementType = callback.id };
        InteropFunction install = new(new("install"), "native_install", empty.id, [new(new("callback-argument"), "callback", pointer.id)], "C")
        { sourceName = "install_callback" };
        InteropModule module = new("metadata-browser", "emscripten-wasm32-emscripten", 4, [integer, empty, callback, pointer], [install], [])
        { byteOrder = NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.Metadata", "MetadataApi")
        {
            emitNativeMetadata = true,
            emitCallbackDelegates = false,
            names = ImmutableDictionary<string, string>.Empty.Add("native_install", "Install")
        }, CancellationToken.None);
        ManagedRuntimeProfile profile = new("metadata-browser", new ManagedRuntimeId("fixture.nonblocking-runtime"),
            NativeModuleMechanism.StaticAdapter, [BindingImportMode.DllImport, BindingImportMode.LibraryImport], true, false)
        {
            functionPointerTransport = ManagedFunctionPointerTransport.NativeInteger,
            libraryImportConvention = ManagedImportConventionStrategy.RuntimeDefault,
            defaultCallingConvention = "Cdecl"
        };
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("__Internal", sink) { importMode = mode, managedRuntime = profile };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpPhysicalImportPlan physical = Assert.Single(plan.physicalImports);
        Assert.Equal(new[] { "nint" }, physical.parameterTypes);
        Assert.Contains("delegate*", plan.methods[0].parameterTypes[0], StringComparison.Ordinal);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
        string key = Assert.Single(sink.sources.Keys, path => path.Contains("NativeImports", StringComparison.Ordinal));
        CompilationUnitSyntax imports = CSharpSyntaxTree.ParseText(sink.sources[key]).GetCompilationUnitRoot();
        MethodDeclarationSyntax actual = Assert.Single(imports.DescendantNodes().OfType<MethodDeclarationSyntax>(), method => method.Identifier.ValueText == physical.declarationName);
        MethodDeclarationSyntax forwarder = Assert.Single(imports.DescendantNodes().OfType<MethodDeclarationSyntax>(), method => method.Identifier.ValueText == "Function0");
        Assert.DoesNotContain(forwarder.AttributeLists.SelectMany(list => list.Attributes), IsNative);
        Assert.Equal("install_callback", Value(actual.AttributeLists.Single(list => list.Target is null && list.Attributes.Any(IsNative)).Attributes.Single()));
        Assert.Equal(new[] { "callback", "int (*)(int)" }, actual.ParameterList.Parameters[0].AttributeLists.SelectMany(list => list.Attributes).Select(Value));
        AttributeListSyntax metadata = actual.AttributeLists.Single(list => list.Target is null && list.Attributes.Any(IsNative));
        string changed = imports.ReplaceNode(forwarder, forwarder.AddAttributeLists(metadata)).ToFullString();
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
            sink.sources.Select(source => (source.Key, source.Key == key ? changed : source.Value)), request.libraryName));
    }

    private static ManagedApiModel Model(
        bool enabled,
        bool sourceRuntime
    ) {
        ManagedApiModel original = NeutralBackendTests.Model();
        InteropFunction function = original.interop.functions[0] with { sourceQualifiedName = "native::operator\"\"_tag" };
        InteropModule interop = new(original.interop.libraryId, original.interop.targetId, original.interop.pointerSize,
            original.interop.types, original.interop.functions.SetItem(0, function), original.interop.diagnostics)
        {
            byteOrder = original.interop.byteOrder,
            constants = original.interop.constants,
            functionAliases = original.interop.functionAliases,
            stringConstants = original.interop.stringConstants,
            virtualProxies = original.interop.virtualProxies
        };
        ManagedApiModel model = new DefaultManagedProjection().Project(interop, new("Generated.Example", "NativeApi")
        {
            emitNativeMetadata = enabled,
            nestGeneratedTypesInApi = sourceRuntime,
            style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
            names = ImmutableDictionary<string, string>.Empty.Add(function.exportName, "ManagedAdd")
        }, CancellationToken.None);
        return model with { methods = model.methods.SetItem(0, model.methods[0] with { name = "ManagedAdd", parameterNames = ["renamedLeft", "renamedRight"] }) };
    }

    private static EmissionRequest Request(
        BindingImportMode mode,
        bool sourceRuntime,
        NeutralBackendTests.MemorySink sink
    )
        => new("metadata_fixture", sink)
        {
            importMode = mode,
            includeRuntimeSource = sourceRuntime,
            singleFile = sourceRuntime,
            runtimeNamespace = sourceRuntime ? "Generated.Example.CustomRuntime" : "BGCS.Runtime"
        };
    private static CompilationUnitSyntax[] Roots(NeutralBackendTests.MemorySink sink)
        => sink.sources.Values.Select(source => CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot()).ToArray();
    private static bool IsNative(AttributeSyntax attribute) => attribute.Name.ToString().EndsWith(".NativeNameAttribute", StringComparison.Ordinal);
    private static string Value(AttributeSyntax attribute) => (string)((LiteralExpressionSyntax)attribute.ArgumentList!.Arguments[1].Expression).Token.Value!;
}
