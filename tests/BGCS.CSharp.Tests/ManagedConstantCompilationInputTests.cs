using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ManagedConstantCompilationInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalSourceAndMetadataConstantsBecomeInertValuesWithoutLoadingUserAssemblies(bool metadata)
    {
        string name = "BGCS_ConstantMetadata_" + Guid.NewGuid().ToString("N");
        const string source = """
            namespace Vendor {
                public static class Values {
                    public const long Answer = 43L;
                    [global::System.Runtime.CompilerServices.ModuleInitializer]
                    internal static void Initialize() => throw new global::System.InvalidOperationException("Consumer module must never execute during generation.");
                }
            }
            """;
        ManagedProjectionInputs inputs = metadata ? new() { assemblies = [AssemblyInput(name, source)] }
            : new() { sources = [Source(source)] };
        CSharpEmissionPlan plan = Plan(inputs);
        CSharpConstantPlan field = Assert.Single(plan.constants);
        Assert.Equal("long", field.type);
        Assert.Equal("43L", field.expression);
        Assert.DoesNotContain(AssemblyLoadContext.All.SelectMany(context => context.Assemblies), assembly => assembly.GetName().Name == name);
    }

    [Fact]
    public void ConditionalSourceSymbolsAreCapturedAndChangeTheEstablishedValue()
    {
        ManagedSourceInput source = Source("""
            namespace Vendor { public static class Values {
            #if ALTERNATE
                public const long Answer = 44L;
            #else
                public const long Answer = 43L;
            #endif
            } }
            """);
        Assert.Equal("43L", Assert.Single(Plan(new() { sources = [source] }).constants).expression);
        Assert.Equal("44L", Assert.Single(Plan(new() { sources = [source], defines = ["ALTERNATE"] }).constants).expression);
    }

    [Fact]
    public void UnrelatedGeneratedContextPartialMethodBodiesDoNotPretendToBeConstantDependencies()
    {
        ManagedProjectionInputs inputs = new() { sources = [Source("""
            namespace Vendor { public static class Values { public const long Answer = 43L; } }
            namespace Generated.External { public sealed partial class ExternalApi {
                public string? Describe() => Raw.ToString();
            } }
            """)] };
        // Raw exists in the full generated binding, outside the constant-only
        // compiler skeleton. Its method body cannot establish a C# constant.
        Assert.Equal("43L", Assert.Single(Plan(inputs).constants).expression);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("text")]
    [InlineData("duplicate")]
    [InlineData("relative")]
    [InlineData("symbol")]
    [InlineData("directive")]
    [InlineData("selected")]
    public void InvalidInputClaimsAndSelectedDependenciesFailBeforeEmission(string invalid)
    {
        ManagedSourceInput source = Source("namespace Vendor { public static class Values { public const long Answer = 43L; } }");
        ManagedProjectionInputs inputs = new() { sources = [source] };
        inputs = invalid switch
        {
            "digest" => inputs with { sources = [source with { digest = new(new string('0', 64)) }] },
            "text" => inputs with { sources = [source with { text = source.text.Replace("43L", "44L", StringComparison.Ordinal) }] },
            "duplicate" => inputs with { sources = [source, source] },
            "relative" => inputs with { sources = [source with { path = "relative.cs" }] },
            "symbol" => inputs with { defines = ["INVALID-SYMBOL"] },
            "directive" => inputs with { sources = [Source("#r \"unknown.dll\"\n" + source.text)] },
            _ => inputs with { sources = [Source(source.text.Replace("43L", "MissingValue", StringComparison.Ordinal))] }
        };
        Assert.Throws<ArgumentException>(() => Plan(inputs));
    }

    [Fact]
    public void AnUnselectedMissingAssemblyDependencyCannotMasqueradeAsACompleteClosure()
    {
        string dependencyName = "BGCS_ConstantDependency_" + Guid.NewGuid().ToString("N");
        ManagedAssemblyInput dependency = AssemblyInput(dependencyName, "namespace Vendor.Dependency { public sealed class Token { } }");
        ManagedAssemblyInput facade = AssemblyInput("BGCS_ConstantFacade_" + Guid.NewGuid().ToString("N"), """
            namespace Vendor { public static class Values {
                public const long Answer = 43L;
                public static global::Vendor.Dependency.Token Create() => new();
            } }
            """, [MetadataReference.CreateFromImage(dependency.image)]);
        ArgumentException failure = Assert.Throws<ArgumentException>(() => Plan(new() { assemblies = [facade] }));
        Assert.Contains("closure is incomplete", failure.Message, StringComparison.Ordinal);
        Assert.Contains(dependencyName, failure.Message, StringComparison.Ordinal);
        Assert.Equal("43L", Assert.Single(Plan(new() { assemblies = [facade, dependency] }).constants).expression);
    }

    [Fact]
    public void SameLogicalSourcePathWithDifferentFrozenBytesChangesItsValue()
    {
        ManagedSourceInput first = Source("namespace Vendor { public static class Values { public const long Answer = 43L; } }");
        ManagedSourceInput second = Source(first.text.Replace("43L", "44L", StringComparison.Ordinal), first.path);
        Assert.NotEqual(first.digest, second.digest);
        Assert.Equal("43L", Assert.Single(Plan(new() { sources = [first] }).constants).expression);
        Assert.Equal("44L", Assert.Single(Plan(new() { sources = [second] }).constants).expression);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void OptionalFrameworkForwardersRemainMissingSymbolsWhileExternalClosuresStayStrict(
        bool framework,
        bool selected
    ) {
        string dependencyName = "OptionalForwardedValues" + Guid.NewGuid().ToString("N");
        ManagedAssemblyInput dependency = AssemblyInput(dependencyName,
            "namespace Forwarded { public static class Values { public const long Answer = 99L; } }");
        ManagedAssemblyInput facade = AssemblyInput("OptionalForwardingFacade" + Guid.NewGuid().ToString("N"),
            "[assembly: global::System.Runtime.CompilerServices.TypeForwardedTo(typeof(global::Forwarded.Values))]",
            [MetadataReference.CreateFromImage(dependency.image)]) with
        { isFramework = framework };
        string corePath = typeof(object).Assembly.Location;
        ManagedSourceInput source = Source("namespace Vendor { public static class Values { public const long Answer = "
            + (selected ? "global::Forwarded.Values.Answer" : "43L") + "; } }");
        ManagedProjectionInputs inputs = new()
        {
            assemblies = [Framework(corePath), Framework(Path.Combine(Path.GetDirectoryName(corePath)!, "System.Runtime.dll")), facade],
            sources = [source]
        };
        if (framework && !selected) Assert.Equal("43L", Assert.Single(Plan(inputs).constants).expression);
        else
        {
            ArgumentException failure = Assert.Throws<ArgumentException>(() => Plan(inputs));
            Assert.Contains(selected ? "Forwarded" : "closure is incomplete", failure.Message, StringComparison.Ordinal);
        }

        static ManagedAssemblyInput Framework(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            return new(path, System.Reflection.AssemblyName.GetAssemblyName(path).FullName!,
                new(Convert.ToHexStringLower(SHA256.HashData(bytes))), bytes.ToImmutableArray(), true);
        }
    }

    private static CSharpEmissionPlan Plan(ManagedProjectionInputs inputs)
    {
        InteropModule interop = new("external", "windows-x64-msvc", 8, [], [], [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian, constants = [new(new("selected"), "SELECTED", "SELECTED", new(NativeScalarConstantKind.Integer, "42", 4, true, new(EvidenceLevel.Declared, "native:compiler")), null)] };
        ManagedApiModel model = new DefaultManagedProjection().Project(interop, new("Generated.External", "ExternalApi")
        { constants = [new("SELECTED") { managedType = "long", expression = "global::Vendor.Values.Answer" }] }, CancellationToken.None);
        return new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink()) { managedProjection = inputs });
    }

    private static ManagedSourceInput Source(
        string text,
        string? path = null
    ) {
        byte[] image = Encoding.UTF8.GetBytes(text);
        return new(path ?? Path.Combine(Path.GetTempPath(), "bgcs-constant-source-" + Guid.NewGuid().ToString("N") + ".cs"),
            new(Convert.ToHexStringLower(SHA256.HashData(image))), image.ToImmutableArray(), text);
    }

    private static ManagedAssemblyInput AssemblyInput(
        string name,
        string source,
        ImmutableArray<MetadataReference> additional = default
    ) {
        string frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => Path.GetDirectoryName(path) == frameworkDirectory).Select(path => MetadataReference.CreateFromFile(path)).Cast<MetadataReference>();
        CSharpCompilation compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source, new(LanguageVersion.CSharp14))],
            references.Concat(additional.IsDefault ? [] : additional), new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream stream = new();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        byte[] image = stream.ToArray();
        return new(Path.Combine(Path.GetTempPath(), name + ".dll"), name + ", Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
            new(Convert.ToHexStringLower(SHA256.HashData(image))), image.ToImmutableArray(), false);
    }
}
