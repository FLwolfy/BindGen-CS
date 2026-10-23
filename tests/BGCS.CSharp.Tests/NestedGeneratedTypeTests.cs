using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

public sealed class NestedGeneratedTypeTests
{
    [Theory]
    [InlineData(false, false, ManagedApiStyle.Instance)]
    [InlineData(false, true, ManagedApiStyle.Instance)]
    [InlineData(true, false, ManagedApiStyle.Instance)]
    [InlineData(true, true, ManagedApiStyle.Instance)]
    [InlineData(false, false, ManagedApiStyle.Static)]
    [InlineData(false, true, ManagedApiStyle.Static)]
    [InlineData(true, false, ManagedApiStyle.Static)]
    [InlineData(true, true, ManagedApiStyle.Static)]
    public async Task ScopeChangesTypeIdentityWhilePreservingStorageAndExactDelegatePrototype(bool nested, bool sourceRuntime, ManagedApiStyle style)
    {
        ManagedApiModel model = Model(nested, style);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { singleFile = sourceRuntime, includeRuntimeSource = sourceRuntime,
            runtimeNamespace = sourceRuntime ? "Generated.Example.Runtime" : "BGCS.Runtime" };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        CSharpCompilation compilation = Compile(sink);
        string prefix = "Generated.Example." + (nested ? "NativeApi+" : string.Empty);
        INamedTypeSymbol outer = compilation.GetTypeByMetadataName(prefix + "Outer")!;
        Assert.NotNull(outer);
        Assert.Equal(96, Assert.Single(outer.GetAttributes(), attribute => attribute.AttributeClass!.Name == "StructLayoutAttribute")
            .NamedArguments.Single(argument => argument.Key == "Size").Value.Value);
        IFieldSymbol field = Assert.IsAssignableFrom<IFieldSymbol>(Assert.Single(outer.GetMembers("nested")));
        Assert.True(SymbolEqualityComparer.Default.Equals(compilation.GetTypeByMetadataName(prefix + "Aligned"), field.Type));
        Assert.Equal(32, Assert.Single(field.GetAttributes(), attribute => attribute.AttributeClass!.Name == "FieldOffsetAttribute").ConstructorArguments[0].Value);
        INamedTypeSymbol declaration = compilation.GetTypeByMetadataName(prefix + "NativeCallback")!;
        Assert.NotNull(declaration?.DelegateInvokeMethod);
        IPointerTypeSymbol callbackPointer = Assert.IsAssignableFrom<IPointerTypeSymbol>(declaration!.DelegateInvokeMethod!.Parameters[0].Type);
        Assert.True(SymbolEqualityComparer.Default.Equals(outer, callbackPointer.PointedAtType));
        Assert.Equal(ulong.MaxValue, compilation.GetTypeByMetadataName(prefix + "Flags")!.GetMembers("all").OfType<IFieldSymbol>().Single().ConstantValue);
        Assert.Equal(1UL << 63, compilation.GetTypeByMetadataName(prefix + "WideFlags")!.GetMembers("High").OfType<IFieldSymbol>().Single().ConstantValue);
        Assert.Same(model.interop, plan.api.interop);
        if (nested) Assert.Null(compilation.GetTypeByMetadataName("Generated.Example.Outer"));
        if (sourceRuntime) Assert.Single(sink.sources);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("visibility")]
    [InlineData("lifetime")]
    [InlineData("offset")]
    [InlineData("delegate")]
    public async Task ProcessorsCannotMoveOrChangeNestedAbiDeclarations(string fault)
    {
        ManagedApiModel model = Model(true, ManagedApiStyle.Instance);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string path = fault == "delegate" ? "Types/NativeCallback.g.cs" : "Types/Outer.g.cs";
        CompilationUnitSyntax root = SyntaxFactory.ParseCompilationUnit(sink.sources[path]);
        ClassDeclarationSyntax owner = Assert.Single(root.DescendantNodes().OfType<ClassDeclarationSyntax>());
        CompilationUnitSyntax changed;
        if (fault == "owner") changed = root.ReplaceNode(owner, owner.WithIdentifier(SyntaxFactory.Identifier("OtherApi")));
        else if (fault is "visibility" or "lifetime")
            changed = root.ReplaceToken(Assert.Single(owner.Modifiers, modifier => modifier.IsKind(fault == "visibility" ? SyntaxKind.PublicKeyword : SyntaxKind.SealedKeyword)),
                SyntaxFactory.Token(fault == "visibility" ? SyntaxKind.InternalKeyword : SyntaxKind.StaticKeyword));
        else if (fault == "offset")
        {
            AttributeSyntax offset = Assert.Single(root.DescendantNodes().OfType<FieldDeclarationSyntax>(), field => field.Declaration.Variables[0].Identifier.ValueText == "nested").AttributeLists[0].Attributes[0];
            changed = root.ReplaceNode(offset.ArgumentList!.Arguments[0].Expression, SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(4)));
        }
        else
        {
            DelegateDeclarationSyntax declaration = Assert.Single(root.DescendantNodes().OfType<DelegateDeclarationSyntax>());
            changed = root.ReplaceNode(declaration.ReturnType, SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.LongKeyword)));
        }
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
            sink.sources.Select(source => (source.Key, source.Key == path ? changed.NormalizeWhitespace().ToFullString() : source.Value)), "native"));
    }

    [Theory]
    [InlineData(ManagedApiStyle.Instance, "Raw")]
    [InlineData(ManagedApiStyle.Instance, "Load")]
    [InlineData(ManagedApiStyle.Instance, "NativeLibraryName")]
    [InlineData(ManagedApiStyle.Static, "Raw")]
    [InlineData(ManagedApiStyle.Static, "Load")]
    [InlineData(ManagedApiStyle.Static, "NativeLibraryName")]
    public async Task NestedTypeAndApiMemberConflictsFailBeforeCandidateWrites(ManagedApiStyle style, string name)
    {
        ManagedApiModel model = Model(true, style);
        var index = model.types.IndexOf(model.types.Single(type => model.interop.GetType(type.nativeType).category == NativeTypeCategory.Record && type.name == "Outer"));
        model = model with { types = model.types.SetItem(index, model.types[index] with { name = name }) };
        NeutralBackendTests.MemorySink sink = new();
        Exception? failure = await Record.ExceptionAsync(async () => await new CSharpTestBackend().EmitAsync(model, new("native", sink), CancellationToken.None));
        Assert.True(failure is ArgumentException or InvalidOperationException, failure?.ToString());
        Assert.Empty(sink.sources);
    }

    private static ManagedApiModel Model(bool nested, ManagedApiStyle style)
    {
        InteropModule original = NeutralBackendTests.ModelWithStorage().interop;
        InteropType outer = original.types.Single(type => type.declarationName == "Outer");
        InteropType pointer = new(new("outer-pointer"), "Outer*", NativeTypeCategory.Pointer, 8, 8) { elementType = outer.id };
        InteropType callable = new(new("callback"), "int(Outer*)", NativeTypeCategory.Function, null, null)
            { returnType = original.types[0].id, parameterTypes = [pointer.id], callingConvention = "C" };
        InteropType functionPointer = new(new("callback-pointer"), "int(*)(Outer*)", NativeTypeCategory.Pointer, 8, 8) { elementType = callable.id };
        InteropType alias = new(new("callback-alias"), "NativeCallback", NativeTypeCategory.Alias, 8, 8) { declarationName = "NativeCallback", elementType = functionPointer.id };
        InteropModule module = new(original.libraryId, original.targetId, original.pointerSize,
            original.types.AddRange(ImmutableArray.Create(pointer, callable, functionPointer, alias)), original.functions, []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        return new DefaultManagedProjection().Project(module, new("Generated.Example", "NativeApi")
        { nestGeneratedTypesInApi = nested, style = style, enums = [new("WideFlags", ManagedIntegralKind.UInt64, [new("High", "9223372036854775808")])] }, CancellationToken.None);
    }

    private static CSharpCompilation Compile(NeutralBackendTests.MemorySink sink)
    {
        string[] platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("NestedTypes_" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, new(LanguageVersion.CSharp14), source.Key)),
            platform.Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return compilation;
    }
}
