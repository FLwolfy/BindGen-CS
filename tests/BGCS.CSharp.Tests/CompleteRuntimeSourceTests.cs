using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using BGCS.CSharp.Emission;
using BGCS.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class CompleteRuntimeSourceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeSourceDeliveryRetainsOriginalNoticesAfterNamespaceAndFileComposition(bool singleFile)
    {
        var sources = new RuntimeSourceCatalog().GetSources(["runtime.complete"], "Local.Attributed.Runtime");
        string[] attributed = ["Bool8.cs", "Bool32.cs", "Pointer.cs", "ConstPointer.cs", "Bitfield.cs", "Utils.cs", "NativeNameAttribute.cs"];
        foreach (string file in attributed)
        {
            string text = sources.Single(source => source.Path == "Runtime/" + file).Text;
            Assert.Contains("Copyright (c) 2023 Juna Meinhold", text, StringComparison.Ordinal);
            Assert.Contains("The above copyright notice and this permission notice", text, StringComparison.Ordinal);
        }
        if (singleFile)
        {
            string composed = new SingleFileComposer().ComposeSources(
                sources.Select(source => (source.Path, "#nullable enable\n" + source.Text)), "Local.Attributed");
            Assert.Contains("Copyright (c) 2023 Juna Meinhold", composed, StringComparison.Ordinal);
            Assert.Contains("The above copyright notice and this permission notice", composed, StringComparison.Ordinal);
            using SourceAssembly compiled = Compile([("Bindings.cs", composed)]);
            Assert.NotEmpty(compiled.assembly.GetExportedTypes());
        }
        else
        {
            using SourceAssembly compiled = Compile(sources);
            Assert.NotEmpty(compiled.assembly.GetExportedTypes());
        }
    }

    [Fact]
    public void CompleteSourceDeliveryPreservesTheRuntimePackagesPublicTypesAndMembers()
    {
        var sources = new RuntimeSourceCatalog().GetSources(["runtime.complete"], "Local.One.Runtime");
        Assert.DoesNotContain(sources, source => source.Path == "Runtime/Atomic.cs");
        Assert.DoesNotContain(sources, source => source.Path == "Runtime/Iterator.cs");
        Assert.Contains(sources, source => source.Path == "Runtime/NativeAlignedBuffer.cs");
        Assert.Contains(sources, source => source.Path == "Runtime/NativeCallbackRegistry.cs");
        Assert.DoesNotContain(sources, source => source.Path.EndsWith("EventHandlerList.cs", StringComparison.Ordinal));
        using SourceAssembly source = Compile(sources);
        string[] expected = Surface(typeof(NativeModuleSession).Assembly, "BGCS.Runtime");
        Assert.Equal(expected, Surface(source.assembly, "Local.One.Runtime"));
    }

    [Fact]
    public void CompleteSourceNamespacesCoexistAndPreservePrimitiveAndAllocatorBehavior()
    {
        RuntimeSourceCatalog catalog = new();
        var sources = catalog.GetSources(["runtime.complete"], "Local.One.Runtime")
            .Select(source => ("One/" + source.Path, source.Text))
            .Concat(catalog.GetSources(["runtime.complete"], "Local.Two.Runtime").Select(source => ("Two/" + source.Path, source.Text))).ToImmutableArray();
        const string probe = """
            namespace Local
            {
                public class System { }
                public class BGCS { }
                public static unsafe class Probe
                {
                    public static int Run()
                    {
                        if (typeof(One.Runtime.Bool8) == typeof(Two.Runtime.Bool8)) return -1;
                        One.Runtime.Bool8 enabled = true;
                        if (!(bool)enabled) return -2;
                        int native = 42;
                        One.Runtime.Pointer<int> pointer = new(&native);
                        if (*pointer.handle != 42) return -3;
                        using var storage = new One.Runtime.NativeAlignedBuffer(13, 64);
                        if ((nuint)storage.address % 64 != 0 || storage.byteLength != 13) return -4;
                        using var text = Two.Runtime.Utf32Codec.EncodeCounted("A\U0001F600", false);
                        if (text.encodedCodeUnitCount != 2 || *(uint*)text.pointer != 65 || ((uint*)text.pointer)[1] != 0x1F600) return -5;
                        return native;
                    }
                }
            }
            """;
        sources = sources.Add(("Probe.cs", probe));
        using SourceAssembly source = Compile(sources);
        Assert.Equal(42, source.assembly.GetType("Local.Probe", throwOnError: true)!.GetMethod("Run")!.Invoke(null, null));
    }

    [Fact]
    public void PublicFeatureIdsAreStableUniqueAndEveryOptionalClosureIsSelfContained()
    {
        ImmutableArray<string> features = RuntimeSourceCatalog.GetFeatureIds();
        Assert.Equal(features.Order(StringComparer.Ordinal), features);
        Assert.Equal(features.Length, features.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("runtime.complete", features);
        RuntimeSourceCatalog catalog = new();
        foreach (string feature in features.Where(feature => feature.StartsWith("primitives.", StringComparison.Ordinal)
            || feature is "memory.aligned-buffer" or "metadata.source-location" or "callbacks.delegate-registry" or "callbacks.aot-address" or "utilities.raw-interop"))
        {
            using SourceAssembly source = Compile(catalog.GetSources([feature], "Optional.Runtime"));
            Assert.NotEmpty(source.assembly.GetExportedTypes());
        }
        Assert.Throws<InvalidOperationException>(() => catalog.GetSources(["Runtime.Complete"], "Missing.Runtime"));
        Assert.Throws<InvalidOperationException>(() => catalog.GetSources(["primitives.atomic"], "Missing.Runtime"));
    }

    private static string[] Surface(
        Assembly assembly,
        string sourceNamespace
    ) {
        List<string> surface = [];
        foreach (Type type in assembly.GetExportedTypes())
        {
            surface.Add(type.FullName!.Replace(sourceNamespace, "Runtime", StringComparison.Ordinal) + ":" + type.Attributes);
            foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                surface.Add(type.FullName!.Replace(sourceNamespace, "Runtime", StringComparison.Ordinal) + ":" + member.MemberType + ":" + member.ToString()!.Replace(sourceNamespace, "Runtime", StringComparison.Ordinal));
        }
        return surface.Order(StringComparer.Ordinal).ToArray();
    }

    private static SourceAssembly Compile(ImmutableArray<(string Path, string Text)> sources)
    {
        string frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string platformPaths = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Framework metadata is unavailable.");
        var references = platformPaths.Split(Path.PathSeparator)
            .Where(path => string.Equals(Path.GetDirectoryName(path), frameworkDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .Select(path => MetadataReference.CreateFromFile(path));
        var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source.Text, new CSharpParseOptions(LanguageVersion.CSharp14), source.Path));
        CSharpCompilation compilation = CSharpCompilation.Create("Complete.Runtime.Source." + Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext context = new("complete-runtime-source", isCollectible: true);
        try
        {
            Assembly assembly = context.LoadFromStream(image);
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name?.StartsWith("BGCS", StringComparison.Ordinal) == true);
            return new(context, assembly);
        }
        catch { context.Unload(); throw; }
    }

    private sealed class SourceAssembly(
        AssemblyLoadContext context,
        Assembly assembly
    ) : IDisposable
    {
        public Assembly assembly { get; } = assembly;
        public void Dispose() => context.Unload();
    }
}
