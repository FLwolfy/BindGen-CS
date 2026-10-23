using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class BrowserImportConventionTests
{
    [Fact]
    public async Task BrowserDefaultCdeclLibraryImportRetainsExactAbiAndRejectsInjectedConventionMetadata()
    {
        ManagedApiModel original = NeutralBackendTests.Model();
        InteropModule interop = new(original.interop.libraryId, "opaque-browser-fixture", 4, original.interop.types, original.interop.functions, [])
        { byteOrder = NativeByteOrder.LittleEndian };
        ManagedApiModel model = original with { interop = interop };
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("api", sink)
        {
            importMode = BindingImportMode.LibraryImport,
            managedRuntime = BrowserProfile()
        };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        Assert.True(plan.libraryImportUsesPlatformDefault);
        Assert.All(plan.methods, method => Assert.Equal("Cdecl", method.callingConvention));
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string imports = sink.sources["Imports/NativeApiNativeImports.g.cs"];
        Assert.Contains("LibraryImport(\"api\"", imports);
        Assert.DoesNotContain("UnmanagedCallConv", imports);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(pair => (pair.Key, pair.Value)), request.libraryName);
        string changed = imports.Replace("internal static partial int Function0", "[global::System.Runtime.InteropServices.UnmanagedCallConvAttribute(CallConvs = new global::System.Type[] { typeof(global::System.Runtime.CompilerServices.CallConvStdcall) })]\ninternal static partial int Function0", StringComparison.Ordinal);
        Assert.NotEqual(imports, changed);
        Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
            sink.sources.Select(pair => (pair.Key, pair.Key == "Imports/NativeApiNativeImports.g.cs" ? changed : pair.Value)), request.libraryName));
    }

    [Theory]
    [InlineData("width")]
    [InlineData("runtime")]
    [InlineData("module")]
    [InlineData("convention")]
    public void DefaultConventionCapabilityCannotBeAppliedToUnprovenProfiles(string fault)
    {
        ManagedApiModel original = NeutralBackendTests.Model();
        InteropModule interop = new(original.interop.libraryId, "opaque-browser-fixture", 4, original.interop.types, original.interop.functions, [])
        { byteOrder = NativeByteOrder.LittleEndian };
        ManagedApiModel model = original with { interop = interop };
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = PlanningFixture.Prepare(model, new("api", sink)
        { importMode = BindingImportMode.LibraryImport, managedRuntime = BrowserProfile() });
        ManagedRuntimeProfile profile = request.managedRuntime!;
        if (fault == "runtime") profile = profile with { runtimeId = new ManagedRuntimeId("fixture.static-runtime") };
        if (fault == "module") profile = profile with { moduleMechanism = NativeModuleMechanism.DynamicLibrary };
        if (fault is "width" or "convention")
            model = model with { interop = new(interop.libraryId, interop.targetId, fault == "width" ? 8 : 4, interop.types,
                fault == "convention" ? interop.functions.SetItem(0, interop.functions[0] with { callingConvention = "StdCall" }) : interop.functions, [])
                { byteOrder = NativeByteOrder.LittleEndian } };
        if (fault == "width") Assert.Throws<NotSupportedException>(() => new CSharpEmissionPlanner().Plan(model, request with { managedRuntime = profile }));
        else Assert.Throws<InvalidOperationException>(() => new CSharpEmissionPlanner().Plan(model, request with { managedRuntime = profile }));
        Assert.Empty(sink.sources);
    }

    [Fact]
    public async Task ExplicitDesktopConventionMetadataIsPreserved()
    {
        NeutralBackendTests.MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(NeutralBackendTests.Model(), new("desktop", sink) { importMode = BindingImportMode.LibraryImport }, CancellationToken.None);
        Assert.Contains("UnmanagedCallConv", sink.sources["Imports/NativeApiNativeImports.g.cs"]);
        Assert.Contains("CallConvCdecl", sink.sources["Imports/NativeApiNativeImports.g.cs"]);
    }

    private static ManagedRuntimeProfile BrowserProfile() => new("browser-fixture", new ManagedRuntimeId("fixture.nonblocking-runtime"),
        NativeModuleMechanism.StaticAdapter, [BindingImportMode.LibraryImport], true, false)
    { libraryImportConvention = ManagedImportConventionStrategy.RuntimeDefault, defaultCallingConvention = "Cdecl" };
}
