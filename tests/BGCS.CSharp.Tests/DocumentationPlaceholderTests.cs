using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.CSharp.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class DocumentationPlaceholderTests
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
    public async Task PlaceholderChoicePreservesNativeProseAndCompilesAllForwarders(
        bool enabled,
        bool singleFile,
        ManagedApiStyle style
    ) {
        InteropModule original = NeutralBackendTests.ModelWithStorage().interop;
        SourceDocumentation documentedParameters = new(string.Empty, [new("left", 0, "left <native> input")], "native sum");
        InteropModule interop = new(original.libraryId, original.targetId, original.pointerSize, original.types,
            original.functions.Select(function => function.exportName == "add"
                ? function with { documentationContent = documentedParameters }
                : function with { documentation = "Native documented method." }).ToImmutableArray(), [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(interop,
            new("Generated.Example", "NativeApi") { emitPlaceholderComments = enabled, style = style }, CancellationToken.None);
        var add = Assert.Single(interop.functions, function => function.exportName == "add");
        model = model with
        {
            aliases = [new(add.id, "AliasAdd") { documentation = string.Empty }],
            functionContainers = [new("Math", [new(add.id, "Add") { documentation = string.Empty }])]
        };
        NeutralBackendTests.MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink) { singleFile = singleFile }, CancellationToken.None);
        string[] paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("Placeholder_" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value,
                new(LanguageVersion.CSharp14, documentationMode: DocumentationMode.Diagnose), source.Key)),
            paths.Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var compiled = compilation.Emit(image);
        Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Diagnostics));
        string contextName = style == ManagedApiStyle.Static ? "NativeApiContext" : "NativeApi";
        INamedTypeSymbol context = compilation.GetTypeByMetadataName("Generated.Example." + contextName)!;
        string addDocumentation = context.GetMembers("Add").OfType<IMethodSymbol>().Single().GetDocumentationCommentXml()!;
        Assert.Contains("left &lt;native&gt; input", addDocumentation, StringComparison.Ordinal);
        Assert.Contains("native sum", addDocumentation, StringComparison.Ordinal);
        Assert.Equal(enabled, addDocumentation.Contains("To be documented.", StringComparison.Ordinal));
        string notifyDocumentation = context.GetMembers("Notify").OfType<IMethodSymbol>().Single().GetDocumentationCommentXml()!;
        Assert.Contains("Native documented method.", notifyDocumentation, StringComparison.Ordinal);
        Assert.DoesNotContain("To be documented.", notifyDocumentation, StringComparison.Ordinal);
        string aliasDocumentation = context.GetMembers("AliasAdd").OfType<IMethodSymbol>().Single().GetDocumentationCommentXml()!;
        Assert.Equal(enabled, aliasDocumentation.Contains("To be documented.", StringComparison.Ordinal));
        string recordDocumentation = compilation.GetTypeByMetadataName("Generated.Example.Outer")!.GetDocumentationCommentXml()!;
        Assert.Equal(enabled, recordDocumentation.Contains("To be documented.", StringComparison.Ordinal));
        Assert.Same(interop, model.interop);
        Assert.Same(documentedParameters, add.documentationContent);
        Assert.Equal(string.Empty, add.documentationContent!.summary);
        Assert.Null(interop.types.Single(type => type.declarationName == "Outer").documentation);
    }
}
