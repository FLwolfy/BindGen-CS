using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Managed;
using BGCS.Core.Requests;
using BGCS.CSharp.Projection;
using BGCS.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ManagedRecipeProjectionTests
{
    [Fact]
    public async Task FriendlyConversions_CompileAndInvokeWhileRawCarriersStayExact()
    {
        ManagedApiModel model = NeutralBackendTests.Model();
        ManagedRecipeProjection projection = new("long", "checked((int){value})", "(long){value} + 100");
        model = model with
        {
            methods = model.methods.Select(method => method.name == "Add" ? method with
            {
                valueProjections = [new(new("left"), projection), new(new("right"), projection)],
                returnProjection = projection
            } : method).ToImmutableArray(),
            recipeHelpers = [new("unused-count", "size_t", new("uint", "{value}", "checked((uint){value})"))]
        };
        NeutralBackendTests.MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink), CancellationToken.None);
        Assert.Contains("public long Add(long left, long right)", sink.sources["Api/NativeApi.Methods.g.cs"]);
        Assert.Contains("public int Add(int left, int right)", sink.sources["Api/NativeApi.Raw.g.cs"]);
        Assert.Contains("public static ulong ToNative_unused_count(uint value)", sink.sources["Extensions/ConfiguredLoweringProjections.g.cs"]);
        using CompiledSources compiled = Compile(sink);
        Type api = compiled.assembly.GetType("Generated.Example.NativeApi", true)!;
        using IDisposable instance = (IDisposable)api.GetMethod("FromProvider")!.InvokeFactory([new StaticModuleProvider(Exports())])!;
        Assert.Equal(142L, api.GetMethod("Add")!.Invoke(instance, [20L, 22L]));
        object raw = api.GetProperty("Raw")!.GetValue(instance)!;
        Assert.Equal(42, raw.GetType().GetMethod("Add")!.Invoke(raw, [20, 22]));
        Assert.IsType<OverflowException>(Assert.Throws<TargetInvocationException>(() => api.GetMethod("Add")!.Invoke(instance, [long.MaxValue, 1L])).InnerException);
        Type helpers = api.GetNestedType("RecipeConversions")!;
        Assert.Equal(7UL, helpers.GetMethod("ToNative_unused_count")!.Invoke(null, [(uint)7]));
    }

    [Fact]
    public async Task UnusedMalformedProjection_FailsBeforeCandidateWrites()
    {
        ManagedApiModel model = NeutralBackendTests.Model() with
        { recipeHelpers = [new("unused", "uint32_t", new("uint", "{value} +", "checked((uint){value})"))] };
        NeutralBackendTests.MemorySink sink = new();
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await new CSharpTestBackend().EmitAsync(model, new("unused", sink), CancellationToken.None));
        Assert.Contains("syntactically invalid", failure.Message);
        Assert.Empty(sink.sources);
    }

    [Theory]
    [InlineData(4, "uint")]
    [InlineData(8, "ulong")]
    public void NativeSizeHelpers_UseActualTargetFacts(
        int pointerSize,
        string expected
    ) {
        var plan = new ManagedRecipeProjectionValidator().Plan(new("uint", "{value}", "checked((uint){value})"), "size_t", pointerSize);
        Assert.Equal(expected, plan.carrierType);
    }

    [Fact]
    public void RequiredNamespace_QualifiesPresentationTypeAndPreservesLiteralText()
    {
        var plan = new ManagedRecipeProjectionValidator().Plan(new("DateTime", "checked((long){value}.Ticks)", "new DateTime({value})", "System"), "long", 8);
        Assert.Equal("global::System.DateTime", plan.managedType);
        Assert.Equal("new DateTime(value)", plan.fromNativeExpression);
        var literal = new ManagedRecipeProjectionValidator().Plan(new("string", null, "\"{value}\""), "int", 8);
        Assert.Equal("\"{value}\"", literal.fromNativeExpression);
    }

    [Fact]
    public void AllConfiguredRecipes_AreValidatedEvenWithoutNativeMatches()
    {
        Assert.Throws<InvalidOperationException>(() => new ManagedRecipeProjectionValidator().ValidateRecipes(new[]
        { new TypeLoweringRecipe { id = "unmatched", typePattern = "Absent::*", managedProjection = new("uint", "{value} +", "{value}") } }));
        Assert.Throws<InvalidOperationException>(() => new ManagedRecipeProjectionValidator().Plan(new("int", "{unknown}", null), "int", 8));
    }

    [Fact]
    public void DetachedProjectionAssociations_RoundTripWithoutSpecialDictionaryKeyConverters()
    {
        ManagedApiModel model = NeutralBackendTests.Model();
        ManagedRecipeProjection projection = new("long", "checked((int){value})", "(long){value}");
        model = model with { methods = model.methods.SetItem(0, model.methods[0] with { valueProjections = [new(new("left"), projection)] }) };
        ManagedApiModel restored = JsonSerializer.Deserialize<ManagedApiModel>(JsonSerializer.Serialize(model))!;
        ManagedValueProjection conversion = Assert.Single(restored.methods[0].valueProjections);
        Assert.Equal(new StableId("left"), conversion.nativeParameter);
        Assert.Equal(projection, conversion.projection);
    }

    [Fact]
    public async Task CallablePresentationRulesBindExactParametersAndPreserveCheckedRawCalls()
    {
        ManagedApiModel source = NeutralBackendTests.Model();
        ManagedRecipeProjection projection = new("long", "checked((int){value})", "(long){value} + 100");
        ManagedApiModel model = new DefaultManagedProjection().Project(source.interop, new("Generated.Example", "NativeApi")
        { functions = [new("add") { parameters = [new("left", projection), new("right", projection)], returns = projection }] }, CancellationToken.None);
        Assert.Equal(2, model.methods[0].valueProjections.Length);
        NeutralBackendTests.MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink), CancellationToken.None);
        using CompiledSources compiled = Compile(sink);
        Type api = compiled.assembly.GetType("Generated.Example.NativeApi", true)!;
        using IDisposable instance = (IDisposable)api.GetMethod("FromProvider")!.InvokeFactory([new StaticModuleProvider(Exports())])!;
        Assert.Equal(142L, api.GetMethod("Add")!.Invoke(instance, [20L, 22L]));
        object raw = api.GetProperty("Raw")!.GetValue(instance)!;
        Assert.Equal(42, raw.GetType().GetMethod("Add")!.Invoke(raw, [20, 22]));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing-input")]
    [InlineData("missing-output")]
    public void InvalidCallablePresentationsAreRejectedWithoutChangingTheInteropModel(string invalid)
    {
        ManagedApiModel source = NeutralBackendTests.Model();
        ManagedRecipeProjection input = new("long", "checked((int){value})");
        ManagedFunctionRuleRequest rule = invalid switch
        {
            "unknown" => new("add") { parameters = [new("missing", input)] },
            "duplicate" => new("add") { parameters = [new("left", input), new("left", input)] },
            "missing-input" => new("add") { parameters = [new("left", new("long", null, "(long){value}"))] },
            _ => new("add") { returns = input }
        };
        Assert.ThrowsAny<ArgumentException>(() => new DefaultManagedProjection().Project(source.interop,
            new("Generated.Example", "NativeApi") { functions = [rule] }, CancellationToken.None));
        Assert.Empty(source.methods[0].valueProjections);
        Assert.Null(source.methods[0].returnProjection);
    }

    private static unsafe IEnumerable<KeyValuePair<string, nint>> Exports() => new Dictionary<string, nint>
    {
        ["add"] = (nint)(delegate* unmanaged[Cdecl]<int, int, int>)&Add,
        ["notify"] = (nint)(delegate* unmanaged[Cdecl]<void>)&Notify
    };
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Add(
        int left,
        int right
    ) => left + right;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static void Notify() { }

    private static CompiledSources Compile(NeutralBackendTests.MemorySink sink)
    {
        string[] paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("ManagedRecipes_" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, new(LanguageVersion.CSharp14), source.Key)),
            paths.Append(typeof(NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream stream = new();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        stream.Position = 0;
        AssemblyLoadContext context = new("BGCS.ManagedRecipes.Test", isCollectible: true);
        return new(context, context.LoadFromStream(stream));
    }
    private sealed record CompiledSources(
        AssemblyLoadContext context,
        Assembly assembly
    ) : IDisposable
    { public void Dispose() => context.Unload(); }
}
