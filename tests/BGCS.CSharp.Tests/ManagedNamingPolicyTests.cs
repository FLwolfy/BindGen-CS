using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ManagedNamingPolicyTests
{
    [Theory]
    [InlineData(ManagedNameCasing.Preserve, "HTTPServer2D_value")]
    [InlineData(ManagedNameCasing.Pascal, "HttpServer2DValue")]
    [InlineData(ManagedNameCasing.Camel, "httpServer2DValue")]
    [InlineData(ManagedNameCasing.Snake, "http_server2_d_value")]
    [InlineData(ManagedNameCasing.ScreamingSnake, "HTTP_SERVER2_D_VALUE")]
    [InlineData(ManagedNameCasing.UpperFlat, "HTTPSERVER2DVALUE")]
    [InlineData(ManagedNameCasing.LowerFlat, "httpserver2dvalue")]
    public void AcronymAndDigitBoundariesHaveExplicitInvariantSpellings(ManagedNameCasing casing, string expected)
        => Assert.Equal(expected, ManagedNamingResolver.Convert("HTTPServer2D_value", casing));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CategoryPoliciesAndExactOverridesCompileWithoutChangingNativeStorageOrExports(bool singleFile)
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType record = new(new("record"), "BGCS_HTTP_result_t", NativeTypeCategory.Record, 8, 4)
        {
            declarationName = "BGCS_HTTP_result_t",
            fields = [new(new("x"), "m_native_value", integer.id, 0, 0, null), new(new("y"), "m_override_value", integer.id, 4, 32, null)]
        };
        InteropType enumeration = new(new("enum"), "BGCS_Mode", NativeTypeCategory.Enumeration, 4, 4)
        { declarationName = "BGCS_Mode", elementType = integer.id, enumValues = [new(new("ready"), "BGCS_MODE_READY", "1", "1")] };
        InteropFunction function = new(new("read"), "bgcs_http_request", integer.id, [new(new("input"), "P_ITEM", integer.id)], "C");
        InteropModule native = new("names", "windows-x64-msvc", 8, [integer, record, enumeration], [function], []) { byteOrder = NativeByteOrder.LittleEndian };
        ManagedNamingPolicy policy = new()
        {
            types = new() { casing = ManagedNameCasing.Pascal, removePrefixes = ["BGCS_", "B"], removeSuffixes = ["_t"] },
            enumerations = new() { casing = ManagedNameCasing.Pascal, removePrefixes = ["BGCS_"] },
            enumerationValues = ImmutableDictionary<string, ManagedNameRule>.Empty.Add("BGCS_Mode", new() { casing = ManagedNameCasing.Pascal, removePrefixes = ["BGCS_MODE_"] }),
            fields = new() { casing = ManagedNameCasing.Camel, removePrefixes = ["m_"] },
            functions = new() { casing = ManagedNameCasing.Pascal, removePrefixes = ["bgcs_"] },
            parameters = new() { casing = ManagedNameCasing.Camel, removePrefixes = ["P_"] },
            reservedNames = ["item"], reservedSuffix = "Argument"
        };
        ManagedApiModel model = new DefaultManagedProjection().Project(native, new("Generated.Policy", "Api")
        { naming = policy, names = ImmutableDictionary<string, string>.Empty.Add("BGCS_HTTP_result_t::m_override_value", "event") }, CancellationToken.None);
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, new("unused", new Sink()));
        Assert.Equal("bgcs_http_request", Assert.Single(plan.methods).exportName);
        Assert.Equal("HttpRequest", plan.methods[0].name);
        Assert.Equal("itemArgument", Assert.Single(plan.methods[0].parameterNames));
        Sink sink = new();
        await new CSharpBackend().EmitAsync(model, new("unused", sink) { singleFile = singleFile }, CancellationToken.None);
        CSharpCompilation compilation = CSharpCompilation.Create("NamedPolicy" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, path: source.Key)),
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream output = new();
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assembly assembly = Assembly.Load(output.ToArray());
        Type storage = assembly.GetType("Generated.Policy.HttpResult", true)!;
        Assert.Equal(8, Marshal.SizeOf(storage));
        Assert.Equal(0, Marshal.OffsetOf(storage, "nativeValue").ToInt32());
        Assert.Equal(4, Marshal.OffsetOf(storage, "event").ToInt32());
        Assert.Equal(["Ready"], Enum.GetNames(assembly.GetType("Generated.Policy.Mode", true)!));
        Assert.Equal("itemArgument", Assert.Single(assembly.GetType("Generated.Policy.Api", true)!.GetMethod("HttpRequest")!.GetParameters()).Name);
    }

    [Fact]
    public void InvalidRulesCannotSilentlyFallBackOrProduceAnEmptyIdentifier()
    {
        Assert.Throws<ArgumentException>(() => ManagedNamingResolver.Validate(new() { fields = new() { removePrefixes = [""] } }));
        Assert.Throws<ArgumentException>(() => ManagedNamingResolver.Validate(new() { functions = new() { casing = (ManagedNameCasing)999 } }));
        Assert.Throws<ArgumentException>(() => ManagedNamingResolver.Validate(new() { reservedNames = ["Name"], reservedSuffix = "" }));
        Assert.Equal("中文项目", ManagedNamingResolver.Convert("中文_项目", ManagedNameCasing.Pascal));
    }

    [Fact]
    public void OrderedLiteralReplacementsRunAfterCategoryAffixesAndBeforeReservedNamesAndKeepExactOverrides()
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropModule native = new("replacement_names", "windows-x64-msvc", 8, [integer],
            [new(new("read"), "api_http_request", integer.id, [], "C"), new(new("exact"), "api_exact", integer.id, [], "C")], []);
        ManagedNamingPolicy policy = new()
        {
            functions = new() { casing = ManagedNameCasing.Pascal, removePrefixes = ["api_"], prefix = "Pre", suffix = "Suffix" },
            replacements = [new("http", "Io") { ignoreCase = true }, new("Io", "Net"), new("Pre", "")],
            reservedNames = ["NetRequestSuffix"], reservedSuffix = "Member"
        };
        ManagedApiModel model = new DefaultManagedProjection().Project(native, new("Generated.Replacements", "Api")
        { naming = policy, names = ImmutableDictionary<string, string>.Empty.Add("api_exact", "HttpExact") }, CancellationToken.None);
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, new("unused", new Sink()));
        Assert.Equal("NetRequestSuffixMember", Assert.Single(plan.methods, method => method.exportName == "api_http_request").name);
        Assert.Equal("HttpExact", Assert.Single(plan.methods, method => method.exportName == "api_exact").name);
        Assert.Equal(new[] { "api_http_request", "api_exact" }, native.functions.Select(function => function.exportName));
    }

    [Fact]
    public void ReplacementCollisionsAndEmptyResultsFailBeforeWritingAnyArtifacts()
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropModule native = new("replacement_names", "windows-x64-msvc", 8, [integer],
            [new(new("left"), "left", integer.id, [], "C"), new(new("right"), "right", integer.id, [], "C")], []);
        ManagedApiModel model = new DefaultManagedProjection().Project(native, new("Generated.Replacements", "Api")
        { naming = new() { replacements = [new("Left", "Same"), new("Right", "Same")] } }, CancellationToken.None);
        Sink sink = new();
        Assert.ThrowsAny<Exception>(() => new CSharpEmissionPlanner().Plan(model, new("unused", sink)));
        Assert.Empty(sink.sources);
        Assert.Throws<ArgumentException>(() => new DefaultManagedProjection().Project(native, new("Generated.Replacements", "Api")
        { naming = new() { replacements = [new("Left", "")] } }, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => ManagedNamingResolver.Validate(new() { replacements = [new("", "anything")] }));
    }

    private sealed class Sink : IArtifactSink
    {
        internal Dictionary<string, string> sources { get; } = new(StringComparer.Ordinal);
        public string WriteText(string relativePath, string content) { sources.Add(relativePath, content); return relativePath; }
    }
}
