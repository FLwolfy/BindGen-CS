using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Artifacts;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.CSharp;
using BGCS.CSharp.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class AbiSafetyRegressionTests
{
    [Fact]
    public async Task OverAlignedNestedRecord_PreservesEveryNativeFieldOffset()
    {
        InteropType integer = Integer();
        InteropType octet = new(new("octet"), "unsigned char", NativeTypeCategory.Primitive, 1, 1)
        { primitiveKind = "UnsignedChar", isSigned = false };
        InteropType nested = Aligned(integer);
        InteropType outer = new(new("outer"), "Outer", NativeTypeCategory.Record, 96, 32)
        {
            declarationName = "Outer",
            fields = [new(new("prefix"), "prefix", octet.id, 0, 0, null),
            new(new("nested"), "nested", nested.id, 32, 256, null), new(new("suffix"), "suffix", integer.id, 64, 512, null)]
        };
        InteropModule module = new("regression", "windows-x64-msvc", 8, [integer, octet, nested, outer], [], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(Project(module), new("native", sink), CancellationToken.None);
        string[] platformReferences = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        MetadataReference[] references = platformReferences.Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create("LayoutRegression" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, new(LanguageVersion.CSharp14), source.Key)), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var result = compilation.Emit(image);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext context = new("BGCS.NeutralAbiSafety", isCollectible: true);
        try
        {
            Type generated = context.LoadFromStream(image).GetType("Regression.Outer", throwOnError: true)!;
            Assert.Equal(96, Marshal.SizeOf(generated));
            Assert.Equal((nint)32, Marshal.OffsetOf(generated, "nested"));
            Assert.Equal((nint)64, Marshal.OffsetOf(generated, "suffix"));
        }
        finally
        {
            context.Unload();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverAlignedValueParameter_IsRejectedWhilePointerRemainsRepresentable(bool pointer)
    {
        InteropType integer = Integer();
        InteropType record = Aligned(integer);
        InteropType address = new(new("aligned-address"), "Aligned*", NativeTypeCategory.Pointer, 8, 8) { elementType = record.id };
        InteropType nothing = new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" };
        InteropFunction function = new(new("consume"), "consume", nothing.id, [new(new("value"), "value", pointer ? address.id : record.id)], "C");
        InteropModule module = new("regression", "windows-x64-msvc", 8, [integer, record, address, nothing], [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        MemorySink sink = new();
        if (pointer)
        {
            Assert.NotEmpty(await new CSharpTestBackend().EmitAsync(Project(module), new("native", sink), CancellationToken.None));
            Assert.Contains("Aligned*", string.Join("\n", sink.sources.Values), StringComparison.Ordinal);
        }
        else
        {
            var failure = await Assert.ThrowsAnyAsync<NotSupportedException>(async () => await new CSharpTestBackend().EmitAsync(Project(module), new("native", sink), CancellationToken.None));
            Assert.Contains("complete plan", failure.Message, StringComparison.Ordinal);
            Assert.Empty(sink.sources);
        }
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task LongDoubleValueReturn_IsRejectedWithoutCallingAbiEvidence(BindingImportMode mode)
    {
        InteropType value = new(new("long-double"), "long double", NativeTypeCategory.Primitive, 16, 16) { primitiveKind = "LongDouble" };
        InteropFunction function = new(new("native-value"), "native_value", value.id, [], "C");
        InteropModule module = new("regression", "linux-x64-gnu", 8, [value], [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        MemorySink sink = new();
        var failure = await Assert.ThrowsAnyAsync<NotSupportedException>(async () => await new CSharpTestBackend().EmitAsync(Project(module), new("native", sink) { importMode = mode }, CancellationToken.None));
        Assert.Contains("long double", failure.Message, StringComparison.Ordinal);
        Assert.Empty(sink.sources);
    }

    private static InteropType Integer() => new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
    private static InteropType Aligned(InteropType integer) => new(new("aligned"), "Aligned", NativeTypeCategory.Record, 32, 32)
    { declarationName = "Aligned", fields = [new(new("value"), "value", integer.id, 0, 0, null)] };
    private static BGCS.Core.Managed.ManagedApiModel Project(InteropModule module)
        => new DefaultManagedProjection().Project(module, new("Regression", "Api"), CancellationToken.None);
    private sealed class MemorySink : IArtifactSink
    {
        internal Dictionary<string, string> sources { get; } = new(StringComparer.Ordinal);
        public string WriteText(
            string path,
            string content
        ) { sources.Add(path, content); return "memory:" + path; }
    }
}
