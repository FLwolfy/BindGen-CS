using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using BGCS.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class SharedContextTests
{
    private static readonly ManualResetEventSlim s_callEntered = new(false);
    private static readonly ManualResetEventSlim s_releaseCall = new(false);
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task SharedFacadeRetainsExactInstanceInteropAndCheckedLifecycle(BindingImportMode mode)
    {
        ManagedApiModel model = NeutralBackendTests.Model() with { style = ManagedApiStyle.Static };
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native_fixture", sink) { importMode = mode };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        Assert.Equal("NativeApiContext", plan.contextName);
        Assert.Equal(model, plan.api);
        Assert.Contains("public static unsafe partial class NativeApi", sink.sources["Api/NativeApi.g.cs"]);
        Assert.Contains("public static int Add(int left, int right)", sink.sources["Api/NativeApi.g.cs"]);
        Assert.Contains("public sealed unsafe partial class NativeApiContext", sink.sources["Api/NativeApiContext.g.cs"]);
        Assert.Contains("public int Add(int left, int right)", sink.sources["Api/NativeApiContext.Raw.g.cs"]);
        foreach ((string before, string after) in new[]
        {
            ("Interlocked.Exchange(ref m_context, candidate)", "Interlocked.CompareExchange(ref m_context, candidate, null)"),
            ("Current.Add(left, right)", "Current.Add(right, left)"),
            ("ref m_context, null", "ref m_context, m_context")
        })
        {
            Assert.Contains(before, sink.sources["Api/NativeApi.g.cs"]);
            var changed = sink.sources.Select(pair => (pair.Key, pair.Key == "Api/NativeApi.g.cs" ? pair.Value.Replace(before, after, StringComparison.Ordinal) : pair.Value));
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, request.libraryName));
        }
    }

    [Theory]
    [InlineData("IsInitialized")]
    [InlineData("Current")]
    [InlineData("Install")]
    [InlineData("NativeApi")]
    public async Task SharedLifecycleCollisionsRejectBeforeWriting(string methodName)
    {
        ManagedApiModel model = NeutralBackendTests.Model();
        model = model with { style = ManagedApiStyle.Static, methods = model.methods.SetItem(0, model.methods[0] with { name = methodName }) };
        NeutralBackendTests.MemorySink sink = new();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await new CSharpTestBackend().EmitAsync(model, new("unused", sink), CancellationToken.None));
        Assert.Empty(sink.sources);
    }

    [Fact]
    public async Task SharedReplacementPreservesIndependentContextsAndRejectsPartiallyResolvedCandidates()
    {
        ManagedApiModel model = NeutralBackendTests.Model() with { style = ManagedApiStyle.Static };
        model = model with
        {
            interop = new(model.interop.libraryId, model.interop.targetId, model.interop.pointerSize, model.interop.types, model.interop.functions, model.interop.diagnostics)
            {
                byteOrder = model.interop.byteOrder,
                constants = [new(new("answer"), "ANSWER", "ANSWER",
                new(NativeScalarConstantKind.Integer, "42", 4, true, new(EvidenceLevel.Declared, "fixture:constant")), null)]
            },
            constants = [new(new("answer"), "Answer")],
            recipeHelpers = [new("count", "size_t", new ManagedRecipeProjection("uint", "{value}", "checked((uint){value})"))]
        };
        NeutralBackendTests.MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink), CancellationToken.None);
        // Source contributors may add ordinary helpers to the public partial facade.
        sink.sources.Add("Extensions/FacadeHelper.g.cs", "namespace Generated.Example; public static partial class NativeApi { public static int Marker => 7; }");
        new EmittedInteropContractReader().Verify(new CSharpTestPlanner().Plan(model, new("unused", sink)),
            sink.sources.Select(pair => (pair.Key, pair.Value)), "unused");
        string[] framework = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create("SharedContext_" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(pair => CSharpSyntaxTree.ParseText(pair.Value, new(LanguageVersion.CSharp14), pair.Key)),
            framework.Append(typeof(NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext load = new("BGCS.SharedContext.Test", isCollectible: true);
        try
        {
            Assembly assembly = load.LoadFromStream(image);
            Type facade = assembly.GetType("Generated.Example.NativeApi", true)!;
            Type concrete = assembly.GetType("Generated.Example.NativeApiContext", true)!;
            Assert.True(facade.IsAbstract && facade.IsSealed);
            Assert.Equal(42, facade.GetNestedType("Constants")!.GetField("Answer")!.GetRawConstantValue());
            Assert.Equal(42UL, facade.GetNestedType("RecipeConversions")!.GetMethod("ToNative_count")!.Invoke(null, [42U]));
            Assert.Equal(7, facade.GetProperty("Marker")!.GetValue(null));
            var add = facade.GetMethod("Add")!;
            var initialize = facade.GetMethod("FromProvider")!;
            Assert.False((bool)facade.GetProperty("IsInitialized")!.GetValue(null)!);
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => add.Invoke(null, [20, 22])).InnerException);
            using IDisposable independent = (IDisposable)concrete.GetMethod("FromProvider")!.InvokeFactory([new StaticModuleProvider(Exports(true))])!;
            ObservedProvider original = new(false);
            initialize.Invoke(null, [original]);
            object oldRaw = facade.GetProperty("Raw")!.GetValue(null)!;
            Assert.Equal(42, add.Invoke(null, [20, 22]));
            ObservedProvider missing = new(false, missing: true);
            Assert.IsType<EntryPointNotFoundException>(Assert.Throws<TargetInvocationException>(() => initialize.Invoke(null, [missing])).InnerException);
            Assert.Equal(1, missing.disposals);
            Assert.Equal(0, original.disposals);
            Assert.Equal(42, add.Invoke(null, [20, 22]));
            ObservedProvider replacement = new(true);
            initialize.Invoke(null, [replacement]);
            Assert.Equal(1, original.disposals);
            Assert.Equal(47, add.Invoke(null, [20, 22]));
            Assert.IsType<ObjectDisposedException>(Assert.Throws<TargetInvocationException>(() => oldRaw.GetType().GetMethod("Add")!.Invoke(oldRaw, [20, 22])).InnerException);
            Assert.Equal(47, concrete.GetMethod("Add")!.Invoke(independent, [20, 22]));
            s_callEntered.Reset();
            s_releaseCall.Reset();
            ObservedProvider blocking = new(false, blocking: true);
            initialize.Invoke(null, [blocking]);
            Assert.Equal(1, replacement.disposals);
            Task<object?> active = Task.Run(() => add.Invoke(null, [20, 22]));
            try
            {
                Assert.True(s_callEntered.Wait(TimeSpan.FromSeconds(15)), "The real unmanaged function pointer was not entered.");
                ObservedProvider concurrentReplacement = new(true);
                initialize.Invoke(null, [concurrentReplacement]);
                Assert.Equal(0, blocking.disposals);
                Assert.Equal(47, add.Invoke(null, [20, 22]));
                s_releaseCall.Set();
                Assert.Equal(42, await active.WaitAsync(TimeSpan.FromSeconds(15)));
                Assert.Equal(1, blocking.disposals);
                await (ValueTask)facade.GetMethod("DisposeAsync")!.Invoke(null, null)!;
                Assert.Equal(1, concurrentReplacement.disposals);
            }
            finally { s_releaseCall.Set(); }
            await (ValueTask)facade.GetMethod("DisposeAsync")!.Invoke(null, null)!;
            facade.GetMethod("Dispose")!.Invoke(null, null);
            Assert.Equal(1, replacement.disposals);
            Assert.False((bool)facade.GetProperty("IsInitialized")!.GetValue(null)!);
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => add.Invoke(null, [20, 22])).InnerException);
            Assert.Equal(47, concrete.GetMethod("Add")!.Invoke(independent, [20, 22]));
        }
        finally { load.Unload(); }
    }

    private sealed class ObservedProvider(
        bool alternate,
        bool missing = false,
        bool blocking = false
    ) : INativeModuleProvider
    {
        private readonly StaticModuleProvider m_inner = new(Exports(alternate, blocking));
        internal int disposals;
        public bool TryGetExport(
            string name,
            out nint address
        ) {
            if (missing && name == "notify") { address = 0; return false; }
            return m_inner.TryGetExport(name, out address);
        }
        public void Dispose() { Interlocked.Increment(ref disposals); m_inner.Dispose(); }
    }

    private static unsafe IEnumerable<KeyValuePair<string, nint>> Exports(
        bool alternate,
        bool blocking = false
    ) => new Dictionary<string, nint>
    {
        ["add"] = blocking ? (nint)(delegate* unmanaged[Cdecl]<int, int, int>)&Blocking
            : alternate ? (nint)(delegate* unmanaged[Cdecl]<int, int, int>)&Alternate : (nint)(delegate* unmanaged[Cdecl]<int, int, int>)&Add,
        ["notify"] = (nint)(delegate* unmanaged[Cdecl]<void>)&Notify
    };
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Add(
        int left,
        int right
    ) => left + right;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Alternate(
        int left,
        int right
    ) => left + right + 5;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Blocking(
        int left,
        int right
    ) {
        s_callEntered.Set();
        return s_releaseCall.Wait(TimeSpan.FromSeconds(20)) ? left + right : int.MinValue;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Notify() { }
}
