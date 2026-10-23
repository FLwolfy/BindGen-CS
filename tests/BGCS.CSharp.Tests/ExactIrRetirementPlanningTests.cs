using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ExactIrRetirementPlanningTests
{
    [Fact]
    public async Task ExactSignedThreeBitAndUnsignedOneBitAccessorsShareFourByteStorageWithoutOverwritingEachOther()
    {
        InteropType signed = new(new("signed"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType unsigned = new(new("unsigned"), "unsigned int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "UnsignedInt", isSigned = false };
        InteropType flags = new(new("flags"), "Flags", NativeTypeCategory.Record, 4, 4)
        {
            declarationName = "Flags", recordKind = "Struct",
            fields = [new(new("mode"), "Mode", signed.id, 0, 0, 3), new(new("enabled"), "Enabled", unsigned.id, 0, 3, 1)]
        };
        InteropModule module = new("bits", "windows-x64-msvc", 8, [signed, unsigned, flags], [], []) { byteOrder = NativeByteOrder.LittleEndian };
        var model = new DefaultManagedProjection().Project(module, new("Generated.ExactBits", "Api"), CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("bits", sink), CancellationToken.None);
        string paths = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Framework metadata is unavailable.");
        var references = paths.Split(Path.PathSeparator).Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create("Exact.Signed.Bits." + Guid.NewGuid().ToString("N"),
            sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, new CSharpParseOptions(LanguageVersion.CSharp14), source.Key)), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext load = new("exact-signed-bitfields", isCollectible: true);
        nint memory = Marshal.AllocHGlobal(4);
        try
        {
            Type carrier = load.LoadFromStream(image).GetType("Generated.ExactBits.Flags", throwOnError: true)!;
            Assert.Equal(4, Marshal.SizeOf(carrier));
            PropertyInfo mode = carrier.GetProperty("Mode")!, enabled = carrier.GetProperty("Enabled")!;
            object value = Activator.CreateInstance(carrier)!;
            for (int input = -4; input <= 3; input++)
            {
                mode.SetValue(value, input);
                enabled.SetValue(value, 1u);
                Assert.Equal(input, Assert.IsType<int>(mode.GetValue(value)));
                Assert.Equal(1u, Assert.IsType<uint>(enabled.GetValue(value)));
                Marshal.StructureToPtr(value, memory, fDeleteOld: false);
                Assert.Equal((input & 7) | 8, Marshal.ReadInt32(memory));
                enabled.SetValue(value, 0u);
                Assert.Equal(input, Assert.IsType<int>(mode.GetValue(value)));
                Marshal.StructureToPtr(value, memory, fDeleteOld: false);
                Assert.Equal(input & 7, Marshal.ReadInt32(memory));
            }
        }
        finally { Marshal.FreeHGlobal(memory); load.Unload(); }
    }

    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task AnOpaqueSixteenByteEightAlignedRecordCannotBecomeAByValueImportFromItsExtent(BindingImportMode mode)
    {
        InteropType empty = new(new("void"), "void", NativeTypeCategory.Primitive, 0, 1) { primitiveKind = "Void" };
        InteropType opaque = new(new("hidden"), "NativeHidden", NativeTypeCategory.Record, 16, 8)
        { declarationName = "NativeHidden", recordKind = "Struct", fields = [] };
        InteropFunction consume = new(new("consume"), "consume", empty.id, [new(new("value"), "value", opaque.id)], "C");
        InteropModule module = new("opaque", "windows-x64-msvc", 8, [empty, opaque], [consume], []) { byteOrder = NativeByteOrder.LittleEndian };
        var model = new DefaultManagedProjection().Project(module, new("Generated.ExactOpaque", "Api"), CancellationToken.None);
        NeutralBackendTests.MemorySink sink = new();
        CSharpPlanningException failure = await Assert.ThrowsAsync<CSharpPlanningException>(async () =>
            await new CSharpTestBackend().EmitAsync(model, new EmissionRequest("opaque", sink) { importMode = mode }, CancellationToken.None));
        Assert.Contains("complete plan", failure.Message);
        Assert.Empty(sink.sources);
        Assert.Empty(opaque.fields);
        Assert.Equal(16, opaque.size);
        Assert.Equal(8, opaque.alignment);
    }
}
