using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class AnonymousRecordNamingTests
{
    [Fact]
    public async Task AnonymousSiblingsUseLogicalSourceIdentityAndCompileWithTheirParent()
    {
        ManagedApiModel model = Project(Module("C:/physical/first"));
        ManagedType[] records = model.types.Where(type => type.nativeType != new StableId("int")).ToArray();
        Assert.Equal(3, records.Select(type => type.name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("Parent", model.types.Single(type => type.nativeType == new StableId("parent")).name);
        Assert.All(records, type => Assert.DoesNotContain("physical", type.name, StringComparison.OrdinalIgnoreCase));
        NeutralBackendTests.MemorySink sink = new();
        await new CSharpBackend().EmitAsync(model, new("native", sink), CancellationToken.None);
        CSharpCompilation compilation = CSharpCompilation.Create("AnonymousRecordStorage",
            sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, path: source.Key)),
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var result = compilation.Emit(image);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public void RelocatingCompilerSpellingDoesNotRenameAnAnonymousDeclaration()
    {
        ManagedApiModel first = Project(Module("C:/physical/first"));
        ManagedApiModel second = Project(Module("/physical/second"));
        Assert.Equal(first.types.Select(type => (type.nativeType, type.name)), second.types.Select(type => (type.nativeType, type.name)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASourceTypedefOrExplicitIdentityRemainsThePublicAnonymousTypeName(bool explicitName)
    {
        InteropModule original = Module("C:/physical/first");
        InteropType alias = new(new("public-alias"), "PublicStorage", NativeTypeCategory.Alias, 4, 4)
        { declarationName = "PublicStorage", sourceQualifiedName = "PublicStorage", elementType = new("anonymous-struct"), source = new("native.h", 8, 1) };
        InteropModule interop = new(original.libraryId, original.targetId, original.pointerSize, original.types.Add(alias), [], []);
        ManagedApiModel projected = new DefaultManagedProjection().Project(interop, new("Generated.Anonymous", "Api")
        {
            names = explicitName ? ImmutableDictionary<string, string>.Empty.Add("anonymous-struct", "ChosenRecord")
                : ImmutableDictionary<string, string>.Empty
        }, CancellationToken.None);
        ManagedType type = projected.types.Single(type => type.nativeType == new StableId("anonymous-struct"));
        Assert.Equal(explicitName ? "ChosenRecord" : "PublicStorage", type.name);
        Assert.Equal(explicitName, type.isNameExplicit);
    }

    private static ManagedApiModel Project(InteropModule module) => new DefaultManagedProjection().Project(module,
        new("Generated.Anonymous", "Api"), CancellationToken.None);

    private static InteropModule Module(string physical)
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType first = new(new("anonymous-struct"), "struct Parent::(unnamed at " + physical + "/native.h:5:5)", NativeTypeCategory.Record, 4, 4)
        {
            declarationName = "", sourceQualifiedName = "Parent::", sourceDeclaration = new("decl:first"), source = new("native.h", 5, 5), recordKind = "Struct",
            fields = [new(new("first-value"), "value", integer.id, 0, 0, null)]
        };
        InteropType second = new(new("anonymous-union"), "Parent::", NativeTypeCategory.Record, 4, 4)
        {
            declarationName = "", sourceQualifiedName = "Parent::", sourceDeclaration = new("decl:second"), source = new("native.h", 6, 5), recordKind = "Union",
            fields = [new(new("second-value"), "value", integer.id, 0, 0, null)]
        };
        InteropType parent = new(new("parent"), "struct Parent", NativeTypeCategory.Record, 8, 4)
        {
            declarationName = "Parent", sourceQualifiedName = "Parent", sourceDeclaration = new("decl:parent"), source = new("native.h", 4, 1), recordKind = "Struct",
            fields = [new(new("parent-first"), "child", first.id, 0, 0, null), new(new("parent-second"), "", second.id, 4, 32, null)]
        };
        return new("anonymous", "windows-x64-msvc", 8, [integer, first, second, parent], [], []) { byteOrder = NativeByteOrder.LittleEndian };
    }
}
