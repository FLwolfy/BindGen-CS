using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Managed;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class FunctionTableNamespaceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExactFunctionTableNamespaceUsesGlobalRuntimeTypesWithBuiltInAndCustomProviders(bool sourceRuntime, bool customProvider)
    {
        ManagedApiModel model = NeutralBackendTests.Model() with { @namespace = "Example.FunctionTable" };
        string runtime = sourceRuntime ? "Custom.Runtime" : "BGCS.Runtime";
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("namespace_native", sink) { importMode = BindingImportMode.FunctionTable, includeRuntimeSource = sourceRuntime, runtimeNamespace = runtime };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(pair => (pair.Key, pair.Value)), request.libraryName);
        string probe = """
            namespace Example.FunctionTable
            {
                public class System { }
                public class BGCS { }
                public static unsafe class Probe
                {
                    private static int s_disposed;
                    private static global::System.Collections.Generic.KeyValuePair<string, nint>[] Exports() => [
                        new("add", (nint)(delegate* unmanaged[Cdecl]<int,int,int>)&AddNative),
                        new("notify", (nint)(delegate* unmanaged[Cdecl]<void>)&NotifyNative)];
                    [global::System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(global::System.Runtime.CompilerServices.CallConvCdecl)])]
                    private static int AddNative(int left, int right) => left + right;
                    [global::System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(global::System.Runtime.CompilerServices.CallConvCdecl)])]
                    private static void NotifyNative() { }
                    private sealed class Provider : global::@RUNTIME@.INativeModuleProvider
                    {
                        public bool TryGetExport(string name, out nint address)
                        {
                            foreach (var item in Exports()) if (item.Key == name) { address = item.Value; return true; }
                            address = 0; return false;
                        }
                        public void Dispose() => s_disposed++;
                    }
                    public static int Run()
                    {
                        global::@RUNTIME@.INativeModuleProvider provider = @PROVIDER@;
                        using (var api = NativeApi.FromProvider(provider))
                        {
                            api.Raw.Notify();
                            if (api.Add(17, 25) != 42) return -1;
                        }
                        return @RESULT@;
                    }
                }
            }
            """.Replace("@RUNTIME@", runtime, StringComparison.Ordinal)
            .Replace("@PROVIDER@", customProvider ? "new Provider()" : "new global::" + runtime + ".StaticModuleProvider(Exports())", StringComparison.Ordinal)
            .Replace("@RESULT@", customProvider ? "s_disposed == 1 ? 42 : -2" : "42", StringComparison.Ordinal);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).AsEnumerable();
        if (!sourceRuntime) references = references.Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location);
        CSharpCompilation compilation = CSharpCompilation.Create("ExactFunctionTableNamespace_" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(pair => CSharpSyntaxTree.ParseText(pair.Value, new(LanguageVersion.CSharp14), pair.Key)).Append(CSharpSyntaxTree.ParseText(probe, new(LanguageVersion.CSharp14))),
            references.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext load = new("exact-function-table-namespace", isCollectible: true);
        try
        {
            Assembly consumer = load.LoadFromStream(image);
            Assert.Equal(42, consumer.GetType("Example.FunctionTable.Probe", true)!.GetMethod("Run")!.Invoke(null, null));
            if (sourceRuntime) Assert.DoesNotContain(consumer.GetReferencedAssemblies(), reference => reference.Name is "BGCS.Runtime" or "BGCS" or "BGCS.CSharp");
        }
        finally { load.Unload(); }
    }
}
