using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using BGCS.CSharp.Emission;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class RuntimeAllocatorSourceTests
{
    [Fact]
    public void AllocatorSourceClosure_CompilesAndAllocatesWithoutRuntimePackageOrGeneratorReferences()
    {
        var sources = new RuntimeSourceCatalog().GetSources(["memory.native-allocator"], "Example.Local.Runtime");
        Assert.Equal(8, sources.Length);
        Assert.Contains(sources, source => source.Path == "Runtime/NativeAllocator.cs");
        const string probe = """
            namespace Example.Local
            {
                public class System { }
                public static unsafe class Probe
                {
                    public static int Run()
                    {
                        var allocator = Runtime.NativeAllocator.CreateNativeMemory();
                        using var buffer = allocator.Allocate(13, 64);
                        return buffer.Invoke((address, length) =>
                        {
                            if ((nuint)address % 64 != 0 || length != 13) return -1;
                            int sum = 0;
                            for (nuint index = 0; index < length; ++index)
                            { ((byte*)address)[index] = (byte)(index + 1); sum += ((byte*)address)[index]; }
                            return sum;
                        });
                    }
                }
            }
            """;
        CompileAndRun(sources, probe, 91);
    }

    [Fact]
    public void CustomAllocatorTextSourceClosure_CompilesAndUsesExactMatchingPairWithoutRuntimePackage()
    {
        var sources = new RuntimeSourceCatalog().GetSources(
            ["strings.utf8-input", "strings.utf16-input", "strings.utf32-input"], "Example.Local.Runtime");
        Assert.Equal(14, sources.Length);
        Assert.Single(sources, source => source.Path == "Runtime/NativeTextReader.cs");
        Assert.Single(sources, source => source.Path == "Runtime/NativeTextEncoder.cs");
        Assert.Contains(sources, source => source.Path == "Runtime/NativeAllocator.cs");
        Assert.Contains(sources, source => source.Path == "Runtime/NativeStringLease.cs");
        const string probe = """
            namespace Example.Local
            {
                public class System { }
                public static unsafe class Probe
                {
                    public static int Run()
                    {
                        int allocations = 0, frees = 0;
                        var allocator = new Runtime.NativeAllocator("source-text", (length, alignment) =>
                        {
                            if (length != 64 || alignment != 64) throw new global::System.InvalidOperationException();
                            allocations++;
                            return (nint)global::System.Runtime.InteropServices.NativeMemory.AlignedAlloc(length, alignment);
                        }, (address, length, alignment) =>
                        {
                            if (length != 64 || alignment != 64) throw new global::System.InvalidOperationException();
                            global::System.Runtime.InteropServices.NativeMemory.AlignedFree((void*)address);
                            frees++;
                        }, Runtime.NativeAllocatorThreadDomain.AnyThread);
                        using (var utf8 = Runtime.Utf8Codec.EncodeNullTerminated("A😀", false, 64, allocator))
                        using (var utf16 = Runtime.Utf16Codec.EncodeNullTerminated("A😀", false, false, 64, allocator))
                        using (var utf32 = Runtime.Utf32Codec.EncodeNullTerminated("A😀", false, false, 64, allocator))
                        {
                            if (*(byte*)utf8.pointer != 65 || *(ushort*)utf16.pointer != 65 || *(uint*)utf32.pointer != 65)
                                return -1;
                            if (*((uint*)utf32.pointer + 1) != 0x1F600 || *((uint*)utf32.pointer + 2) != 0) return -2;
                        }
                        return allocations * 10 + frees;
                    }
                }
            }
            """;
        CompileAndRun(sources, probe, 33);
    }

    [Fact]
    public void IndependentRuntimeSourceNamespacesCoexistWithConsumerSystemAndBgcsTypes()
    {
        RuntimeSourceCatalog catalog = new();
        var first = catalog.GetSources(["memory.native-allocator"], "Example.Local.One.Runtime");
        var second = catalog.GetSources(["strings.utf8-input"], "Example.Local.Two.Runtime");
        var combined = first.Select(source => (Path: "One/" + source.Path, source.Text))
            .Concat(second.Select(source => (Path: "Two/" + source.Path, source.Text))).ToImmutableArray();
        const string probe = """
            namespace Example.Local
            {
                public class System { }
                public class BGCS { }
                public static unsafe class Probe
                {
                    public static int Run()
                    {
                        if (typeof(One.Runtime.NativeAllocator) == typeof(Two.Runtime.NativeAllocator)) return -1;
                        var allocator = One.Runtime.NativeAllocator.CreateNativeMemory();
                        using var buffer = allocator.Allocate(13, 32);
                        using var text = Two.Runtime.Utf8Codec.EncodeNullTerminated("A😀", false);
                        int first = buffer.Invoke((address, length) => (nuint)address % 32 == 0 ? (int)length : -100);
                        return first + *(byte*)text.pointer;
                    }
                }
            }
            """;
        CompileAndRun(combined, probe, 78);
    }

    private static void CompileAndRun(System.Collections.Immutable.ImmutableArray<(string Path, string Text)> sources, string probe, int expected)
    {
        string platforms = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("Framework metadata references are unavailable.");
        var references = platforms.Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source.Text, path: source.Path))
            .Append(CSharpSyntaxTree.ParseText(probe, path: "Probe.cs"));
        CSharpCompilation compilation = CSharpCompilation.Create("Allocator.Source.Consumer." + Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));
        using MemoryStream image = new();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        AssemblyLoadContext load = new("allocator-source-probe", isCollectible: true);
        try
        {
            Assembly consumer = load.LoadFromStream(image);
            Assert.Equal(expected, consumer.GetType("Example.Local.Probe", throwOnError: true)!.GetMethod("Run")!.Invoke(null, null));
            Assert.DoesNotContain(consumer.GetReferencedAssemblies(), reference => reference.Name is "BGCS.Runtime" or "BGCS" or "BGCS.CSharp");
        }
        finally { load.Unload(); }
    }
}
