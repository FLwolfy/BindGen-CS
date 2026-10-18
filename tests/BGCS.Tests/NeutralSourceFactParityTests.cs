using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using Xunit;

namespace BGCS.Tests;

public sealed class NeutralSourceFactParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceAliasesAnonymousRecordsAndCallbacksSurvivePublicGenerationAndCompilation(bool singleFile)
    {
        string root = CreateInput();
        try
        {
            await using BindingHost host = Host();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(Configuration(singleFile), root), compileManaged: true);
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.targetModels);
            Assert.DoesNotContain(target.native.declarations, declaration => declaration.name == "hidden_helper");
            Assert.Equal("observe", Assert.Single(target.interop.functions).exportName);
            Assert.DoesNotContain(target.interop.functions, function => function.exportName == "hidden_helper");
            NativeType id = Assert.Single(target.native.types, type => type.category == NativeTypeCategory.Alias && type.spelling == "PublicId");
            Assert.Equal("UnsignedInt", target.native.GetType(id.elementType!.Value).primitiveKind);
            var parent = Assert.Single(target.interop.types, type => type.category == NativeTypeCategory.Record && type.declarationName == "Parent");
            Assert.Equal(12, parent.size);
            Assert.Equal(new long?[] { 0, 4, 8 }, parent.fields.Select(field => field.offset));
            Assert.All(parent.fields, field => Assert.Equal(NativeTypeCategory.Record, target.interop.GetType(field.type).category));
            Assert.DoesNotContain(target.interop.types, type => type.category == NativeTypeCategory.Record && type.declarationName == "OpaqueContext");
            NativeType opaque = Assert.Single(target.native.types, type => type.category == NativeTypeCategory.Alias && type.spelling == "OpaqueContext");
            Assert.Equal("Void", Resolve(opaque).primitiveKind);
            NativeType callback = Resolve(Assert.Single(target.native.types, type => type.category == NativeTypeCategory.Alias && type.spelling == "MessageCallback"));
            Assert.Equal(NativeTypeCategory.Pointer, callback.category);
            NativeType prototype = Resolve(target.native.GetType(callback.elementType!.Value));
            Assert.Equal(NativeTypeCategory.Function, prototype.category);
            Assert.Equal("Void", Resolve(target.native.GetType(prototype.returnType!.Value)).primitiveKind);
            NativeType messagePointer = Resolve(target.native.GetType(Assert.Single(prototype.parameterTypes)));
            Assert.Equal(NativeTypeCategory.Pointer, messagePointer.category);
            NativeType message = Resolve(target.native.GetType(messagePointer.elementType!.Value));
            Assert.Equal(NativeTypeCategory.Record, message.category);
            Assert.Equal("NativeMessageTag", target.native.GetDeclaration(message.declaration!.Value).name);
            Assert.Contains(target.interop.constants, constant => constant.name == "BGCS_LIMIT");
            string managedRoot = Path.Combine(result.outputRoot, target.targetId, "managed");
            string[] files = Directory.GetFiles(managedRoot, "*.cs", SearchOption.AllDirectories);
            if (singleFile) Assert.Equal("SourceFacts.cs", Path.GetFileName(Assert.Single(files)));
            else Assert.True(files.Length > 1);
            string sources = string.Join("\n", files.Select(File.ReadAllText));
            Assert.Contains("delegate* unmanaged[Cdecl]<", sources, StringComparison.Ordinal);
            Assert.DoesNotContain("using OpaqueContext = void", sources, StringComparison.Ordinal);
            Assert.DoesNotContain("HiddenHelper", sources, StringComparison.Ordinal);

            NativeType Resolve(NativeType type)
            {
                while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
                    type = target.native.GetType(type.elementType!.Value);
                return type;
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task AnUnsupportedNativeByValueTypePreservesTheCompletePreviousPublication()
    {
        string root = CreateInput();
        try
        {
            await using BindingHost host = Host();
            GenerationRequest request = new(Configuration(false), root);
            GenerationResult initial = await NativeConsumerHarness.GenerateAndCompileAsync(host, request, compileManaged: true);
            Assert.True(initial.success, initial.GetDiagnosticSummary());
            Dictionary<string, string> previous = Snapshot(initial.outputRoot);
            await File.AppendAllTextAsync(Path.Combine(root, "native.h"),
                "\ntypedef _Complex double NativeComplex;\nNativeComplex unsupported_complex(NativeComplex value);\n");
            GenerationResult rejected = await NativeConsumerHarness.GenerateAndCompileAsync(host, request, compileManaged: true);
            Assert.False(rejected.success);
            Assert.Contains(rejected.diagnostics, diagnostic =>
                diagnostic.message.Contains("NativeComplex", StringComparison.Ordinal)
                    || diagnostic.message.Contains("unsupported_complex", StringComparison.Ordinal));
            Dictionary<string, string> after = Snapshot(initial.outputRoot);
            Assert.Equal(previous.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
            foreach ((string path, string digest) in previous) Assert.Equal(digest, after[path]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static BindingConfiguration Configuration(bool singleFile) => new()
    {
        schemaVersion = 1, library = new() { id = "source-facts", nativeName = "source_facts" },
        source = new() { language = SourceLanguage.C, headers = ["native.h"], includeTransitivelyReferencedHeaders = false,
            selection = new() { excludeDeclarations = ["hidden_helper"] } },
        api = new() { @namespace = "Generated.SourceFacts", name = "FactsApi" },
        output = new() { layout = singleFile ? "singleFile" : "split", singleFileName = "SourceFacts.cs" },
         cache = new() { enabled = false }
    };

    private static string CreateInput()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-source-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "native.h"), """
            #define BGCS_LIMIT 42
            typedef void OpaqueContext;
            typedef unsigned int PublicId;
            typedef struct NativeMessageTag Message;
            typedef void (*MessageCallback)(Message* message);
            typedef struct Parent {
                struct { int value; } child;
                union { float x; int y; } data;
                union { int first; float second; };
            } Parent;
            static inline int hidden_helper(int value) { return value + 1; }
            unsigned int observe(OpaqueContext* context, Parent* parent, PublicId id, MessageCallback callback);
            """);
        return root;
    }

    private static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private static BindingHost Host()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        string workerRoot = Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
        return BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(workerRoot).Build();
    }
}
