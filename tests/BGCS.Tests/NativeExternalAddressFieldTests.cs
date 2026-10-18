using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeExternalAddressFieldTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ClosedGenericPointersAndNativeSizedAddressFieldsCopyWithoutChangingRawCallingAbi(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-external-address-fields-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "vectors.h"), """
                #if defined(_WIN32)
                #define API __declspec(dllexport)
                #else
                #define API __attribute__((visibility("default")))
                #endif
                typedef struct NativeVector_int { int size; int capacity; int* data; } NativeVector_int;
                typedef struct NativeSigned { int size; int capacity; int* data; } NativeSigned;
                typedef struct NativeUnsigned { int size; int capacity; int* data; } NativeUnsigned;
                API NativeVector_int change_vector(NativeVector_int value);
                API NativeSigned change_signed(NativeSigned value);
                API NativeUnsigned change_unsigned(NativeUnsigned value);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "vectors.c"), """
                #include "vectors.h"
                API NativeVector_int change_vector(NativeVector_int value) { value.size+=1;value.capacity+=2;if(value.data)value.data+=1;return value; }
                API NativeSigned change_signed(NativeSigned value) { value.size+=1;value.capacity+=2;if(value.data)value.data+=1;return value; }
                API NativeUnsigned change_unsigned(NativeUnsigned value) { value.size+=1;value.capacity+=2;if(value.data)value.data+=1;return value; }
                """);
            const string consumerTypes = """
                namespace Consumer {
                    public unsafe struct NativeVector<T> where T:unmanaged { public int Capacity; public T* Data; public int Size; }
                    public struct SignedAddress { public int Capacity; public nint Data; public int Size; }
                    public struct UnsignedAddress { public int Capacity; public nuint Data; public int Size; }
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(root, "consumer.cs"), consumerTypes);
            ImmutableDictionary<string, string> fields = ImmutableDictionary<string, string>.Empty.Add("size", "Size").Add("capacity", "Capacity").Add("data", "Data");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "address-copy", nativeName = "address_copy_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["vectors.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new()
                { projectionInputs = new() { sources = ["consumer.cs"] },
                    @namespace = "Generated.AddressCopies", name = "VectorApi", importMode = mode,
                    externalTypes = [new() { nativeType = "NativeVector_int", managedType = "Consumer.NativeVector<int>", fields = fields },
                        new() { nativeType = "NativeSigned", managedType = "Consumer.SignedAddress", fields = fields },
                        new() { nativeType = "NativeUnsigned", managedType = "Consumer.UnsignedAddress", fields = fields }]
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" }, output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(built.success, built.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(built.targetModels);
            Assert.All(target.managed!.externalTypeProjections, projection =>
            {
                Assert.Null(projection.managedStorage);
                ManagedStaticTypeShape observed = Assert.IsType<ManagedStaticTypeShape>(projection.managedShape);
                Assert.Equal(EvidenceLevel.Declared, observed.evidence.level);
                ManagedStaticFieldShape address = Assert.Single(observed.fields, field => field.name == "Data");
                Assert.Equal(ManagedScalarKind.NativeAddress, address.scalarKind);
                Assert.Equal(target.interop.pointerSize, address.size);
            });
            string targetRoot = Path.Combine(built.outputRoot, target.targetId);
            string managed = Path.Combine(targetRoot, "managed");
            string factory = mode == BindingImportMode.FunctionTable ? "VectorApi.Load(binary)" : "VectorApi.Create()";
            string consumer = $$"""
                using Generated.AddressCopies;
                public static unsafe class AddressCopyConsumer {
                    public static int Run(string binary) {
                        using var api={{factory}};
                        int* data=stackalloc int[]{19,23,42};
                        var original=new Consumer.NativeVector<int>{Size=2,Capacity=3,Data=data};
                        var result=api.ChangeVector(original);
                        if(result.Size!=3 || result.Capacity!=5 || result.Data!=data+1 || *result.Data!=23 || original.Data!=data || original.Size!=2)return 1;
                        var signed=api.ChangeSigned(new Consumer.SignedAddress{Size=2,Capacity=3,Data=(nint)data});
                        var unsigned=api.ChangeUnsigned(new Consumer.UnsignedAddress{Size=2,Capacity=3,Data=(nuint)data});
                        if(signed.Size!=3 || signed.Capacity!=5 || signed.Data!=(nint)(data+1))return 2;
                        if(unsigned.Size!=3 || unsigned.Capacity!=5 || unsigned.Data!=(nuint)(data+1))return 3;
                        var empty=api.ChangeVector(default(Consumer.NativeVector<int>));
                        if(empty.Data!=null || empty.Size!=1 || empty.Capacity!=2)return 4;
                        return 42;
                    }
                }
                {{consumerTypes}}
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(managed,
                Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot))), configuration.library.nativeName,
                consumer, "AddressCopyConsumer", root, sourceRuntime));
            if (mode == BindingImportMode.FunctionTable && !sourceRuntime)
            {
                var previous = Directory.GetFiles(built.outputRoot, "*", SearchOption.AllDirectories)
                    .ToDictionary(path => Path.GetRelativePath(built.outputRoot, path), path => SHA256.HashData(File.ReadAllBytes(path)), StringComparer.Ordinal);
                BindingConfiguration invalid = configuration with { api = configuration.api with
                {
                    externalTypes = configuration.api.externalTypes.SetItem(1, configuration.api.externalTypes[1] with { managedType = "Consumer.InvalidAddress" })
                } };
                await File.AppendAllTextAsync(Path.Combine(root, "consumer.cs"), "\nnamespace Consumer { public struct InvalidAddress {public int Capacity;public long Data;public int Size;} }\n");
                GenerationResult rejected = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(invalid, root));
                Assert.False(rejected.success);
                Assert.Contains("scalar kind, signedness and width", rejected.GetDiagnosticSummary(), StringComparison.Ordinal);
                var after = Directory.GetFiles(built.outputRoot, "*", SearchOption.AllDirectories)
                    .ToDictionary(path => Path.GetRelativePath(built.outputRoot, path), path => SHA256.HashData(File.ReadAllBytes(path)), StringComparer.Ordinal);
                Assert.Equal(previous.Keys.Order(), after.Keys.Order());
                foreach (string path in previous.Keys) Assert.Equal(previous[path], after[path]);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
