using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ImportLibraryConstantTests
{
    [Theory]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task FrozenConsumerConstantRemainsADeclarativeImportReference(BindingImportMode mode, bool sourceRuntime)
    {
        ManagedApiModel model = Model("Vendor.LibraryNames.Native") with
        { emitLibraryNameConstant = false, style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance };
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink) { importMode = mode, managedProjection = Inputs(ConsumerSource),
            includeRuntimeSource = sourceRuntime, singleFile = sourceRuntime,
            runtimeNamespace = sourceRuntime ? "Generated.LibraryConstant.Runtime" : "BGCS.Runtime" };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        Assert.Equal("global::Vendor.LibraryNames.Native", plan.importLibraryName!.reference);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        Assert.Contains("global::Vendor.LibraryNames.Native", string.Join("\n", sink.sources.Values), StringComparison.Ordinal);
        Assert.DoesNotContain("public const string NativeLibraryName", string.Join("\n", sink.sources.Values), StringComparison.Ordinal);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), request.libraryName);
        foreach (string replacement in new[] { "\"native\"", "global::Vendor.LibraryNames.Other" })
        {
            var changed = sink.sources.Select(source => (source.Key,
                source.Value.Replace(plan.importLibraryName.reference, replacement, StringComparison.Ordinal))).ToArray();
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, request.libraryName));
        }
        foreach (string shadow in new[] { "namespace Vendor { public static class LibraryNames {public const string Native=\"changed\";} }",
            "public class Vendor { public class LibraryNames {public const string Native=\"changed\";} }" })
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
                sink.sources.Select(source => (source.Key, source.Value)).Append(("shadow.g.cs", shadow)), request.libraryName));
        await ValueReceiverTests.CompileWithSdkAsync(sink, ConsumerSource, sourceRuntime);
    }

    [Theory]
    [InlineData("unqualified", "Native", ConsumerSource)]
    [InlineData("invocation", "Vendor.LibraryNames.Native()", ConsumerSource)]
    [InlineData("injection", "Vendor.LibraryNames.Native; class Escape {}", ConsumerSource)]
    [InlineData("generic", "Vendor.LibraryNames<string>.Native", ConsumerSource)]
    [InlineData("alias", "alias::Vendor.LibraryNames.Native", ConsumerSource)]
    [InlineData("missing", "Vendor.LibraryNames.Missing", ConsumerSource)]
    [InlineData("mismatch", "Vendor.LibraryNames.Native", "namespace Vendor {public static class LibraryNames {public const string Native=\"wrong\";} }")]
    [InlineData("readonly", "Vendor.LibraryNames.Native", "namespace Vendor {public static class LibraryNames {public static readonly string Native=\"native\";} }")]
    [InlineData("property", "Vendor.LibraryNames.Native", "namespace Vendor {public static class LibraryNames {public static string Native=>\"native\";} }")]
    [InlineData("null", "Vendor.LibraryNames.Native", "namespace Vendor {public static class LibraryNames {public const string? Native=null;} }")]
    [InlineData("generated", "Generated.LibraryConstant.LibraryNames.Native", "namespace Generated.LibraryConstant {public static class LibraryNames {public const string Native=\"native\";} }")]
    public void MissingMutableMismatchedOrInjectedReferencesFailBeforeEmission(string caseId, string reference, string source)
    {
        Assert.NotEmpty(caseId);
        NeutralBackendTests.MemorySink sink = new();
        Assert.Throws<ArgumentException>(() => new CSharpTestPlanner().Plan(Model(reference),
            new("native", sink) { importMode = BindingImportMode.DllImport, managedProjection = Inputs(source) }));
        Assert.Empty(sink.sources);
    }

    [Fact]
    public void FunctionTableDoesNotSilentlyIgnoreAStaticImportConstant()
        => Assert.Throws<ArgumentException>(() => new CSharpTestPlanner().Plan(Model("Vendor.LibraryNames.Native"),
            new("native", new NeutralBackendTests.MemorySink()) { managedProjection = Inputs(ConsumerSource) }));

    private const string ConsumerSource = "namespace Vendor {public static class LibraryNames {public const string Native=\"native\";public const string Other=\"native\";} }";

    private static ManagedProjectionInputs Inputs(string source)
    {
        byte[] image = Encoding.UTF8.GetBytes(source);
        return new() { sources = [new(Path.Combine(Path.GetTempPath(), "bgcs-import-constant.cs"),
            new(Convert.ToHexStringLower(SHA256.HashData(image))), image.ToImmutableArray(), source)] };
    }

    private static ManagedApiModel Model(string reference)
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropModule module = new("library-constant", "windows-x64-msvc", 8, [integer], [new(new("answer"), "answer", integer.id, [], "C")], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        return new DefaultManagedProjection().Project(module, new("Generated.LibraryConstant", "Api")
        { importLibraryNameConstant = reference }, CancellationToken.None);
    }
}
