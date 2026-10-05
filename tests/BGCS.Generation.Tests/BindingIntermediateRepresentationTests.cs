using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using BGCS.Configuration;
using BGCS.Configuration.Mapping;
using BGCS.Configuration.Naming;
using BGCS.Emission;
using BGCS.Facade;
using BGCS.Intermediate;
using BGCS.Intermediate.Emission;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public class BindingIntermediateRepresentationTests
{
    [Fact]
    public void Generate_CallbackWithIncompleteRecordAlias_UsesNativePointerCarrier()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-opaque-callback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "api.h");
        File.WriteAllText(header,
            "typedef struct NativeMessageTag Message;\n" +
            "typedef void (*MessageCallback)(Message* message);\n" +
            "void set_message_callback(MessageCallback callback);\n");
        try
        {
            var config = new CsCodeGeneratorConfig
            {
                apiName = "CallbackApi",
                @namespace = "BGCS.Tests.Generated",
                libName = "callback",
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                importType = ImportType.FunctionTable,
                delegatesAsVoidPointer = false,
                singleFileOutputName = "Bindings.cs"
            };
            var generator = new CsCodeGenerator(config);
            Assert.True(generator.Generate(header, Path.Combine(temp, "out")));
            string[] sources = generator.lastResult!.outputFiles.Select(File.ReadAllText).ToArray();
            Assert.Contains("delegate* unmanaged[Cdecl]<nint, void>", string.Join(Environment.NewLine, sources));
            AssertCompiles(sources);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_WindowsCallbackTypedef_EmitsCallableDelegateOverload()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-win-callback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "api.h");
        File.WriteAllText(header,
            "#define API __declspec(dllimport)\n" +
            "typedef int (*BgcsIntCallback)(int value);\n" +
            "API int bgcs_call_callback(BgcsIntCallback callback, int value);\n");
        try
        {
            CsCodeGeneratorConfig config = new()
            {
                apiName = "NativeAbi",
                @namespace = "BGCS.Tests.Generated",
                libName = "native",
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                importType = ImportType.DllImport,
                targetId = "windows-x64-msvc",
                singleFileOutputName = "Bindings.cs"
            };
            config.marshallingMappings["bgcs_call_callback"] = new FunctionMarshallingMapping
            {
                parameters =
                {
                    ["callback"] = new MarshallingMapping
                    {
                        strategy = MarshallingStrategy.Callback,
                        callbackLifetime = BindingCallbackLifetime.CallOnly,
                        callbackThreading = BindingCallbackThreading.CallerThread
                    }
                }
            };
            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, Path.Combine(temp, "out")),
                string.Join(Environment.NewLine, generator.lastResult?.diagnostics.Select(diagnostic => diagnostic.message) ?? []));
            BindingModule module = generator.lastResult!.module!;
            BindingFunction function = Assert.Single(module.functions);
            BindingParameter parameter = Assert.Single(function.parameters, candidate => candidate.nativeName == "callback");
            string source = string.Join(Environment.NewLine, generator.lastResult.outputFiles.Select(File.ReadAllText));
            Assert.Contains("// ABI reference target: windows-x64-msvc", source, StringComparison.Ordinal);
            Assert.True(source.Contains("public static int BgcsCallCallback(BgcsIntCallback callback, int value)", StringComparison.Ordinal),
                $"Delegates: {string.Join(", ", module.delegates.Select(value => value.nativeName + "/" + value.managedName))}; " +
                $"Callback type: {parameter.type.nativeName}/{parameter.type.managedName}; " +
                $"Methods: {string.Join(" | ", source.Split('\n').Where(line => line.Contains("BgcsCallCallback", StringComparison.Ordinal)))}");
            Assert.Contains("global::System.GC.KeepAlive(callback);", source, StringComparison.Ordinal);
            AssertCompiles(generator.lastResult.outputFiles.Select(File.ReadAllText).ToArray());
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_DefaultSafety_KeepsRawAbiButSuppressesUnprovenFriendlyReturn()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-default-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "api.h");
        File.WriteAllText(header, "const char* bgcs_name(void);\n");
        try
        {
            CsCodeGeneratorConfig config = new()
            {
                apiName = "SafeApi",
                @namespace = "BGCS.Tests.Generated",
                libName = "safe",
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                importType = ImportType.DllImport,
                singleFileOutputName = "Bindings.cs"
            };
            Assert.Equal(StrictSafetySeverity.SuppressFriendly, config.strictSafetySeverity);
            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, Path.Combine(temp, "out")));
            BindingFunction function = Assert.Single(generator.lastResult!.module!.functions);
            Assert.True(function.suppressFriendlySurface);
            Assert.Contains(generator.lastResult.diagnostics,
                diagnostic => diagnostic.code == BindingDiagnosticCodes.C_OWNERSHIP);
            string source = string.Join(Environment.NewLine, generator.lastResult.outputFiles.Select(File.ReadAllText));
            Assert.Contains("public static byte* BgcsName()", source);
            Assert.DoesNotContain("public static string? BgcsName()", source);
            AssertCompiles(source);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_DefaultSafety_DoesNotReintroduceSuppressedInstanceOrHandleMembers()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-suppressed-member-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "api.h");
        File.WriteAllText(header,
            "typedef struct NativeThing { int value; } NativeThing; " +
            "NativeThing* NativeThing_Clone(NativeThing* self);");
        try
        {
            CsCodeGeneratorConfig config = new()
            {
                apiName = "SafeApi",
                @namespace = "BGCS.Tests.Generated",
                libName = "safe",
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                importType = ImportType.DllImport,
                singleFileOutputName = "Bindings.cs",
                wrapPointersAsHandle = true,
                memberNamingConvention = NamingConvention.Unknown
            };
            config.functionMappings.Add(new("NativeThing_Clone", "Clone", null, [], []));
            config.knownMemberFunctions["NativeThing"] = ["NativeThing_Clone"];
            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, Path.Combine(temp, "out")));
            BindingFunction function = Assert.Single(generator.lastResult!.module!.functions);
            Assert.True(function.suppressFriendlySurface);
            string[] sources = generator.lastResult.outputFiles.Select(File.ReadAllText).ToArray();
            string source = string.Join(Environment.NewLine, sources);
            Assert.Contains("public static NativeThing* Clone(NativeThing* self)", source);
            Assert.DoesNotContain("public NativeThingPtr Clone(", source);
            Assert.DoesNotContain("public unsafe NativeThing* Clone(", source);
            AssertCompiles(sources);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_DefaultIrBackend_ShouldEmitRawAndFriendlyManagedSurface()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-ir-friendly-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "api.h");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "int bgcs_add(int left, int right);\n" +
            "int bgcs_name_length(const char* name);\n" +
            "const char* bgcs_name(void);\n" +
            "void bgcs_sum(const int* values, int count);\n" +
            "void bgcs_far_size(int size, int kind, int mode, const int* data);\n" +
            "void bgcs_read(int* value);\n");
        try
        {
            CsCodeGeneratorConfig config = new()
            {
                apiName = "FriendlyApi",
                @namespace = "BGCS.Tests.Generated",
                libName = "friendly",
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                importType = ImportType.DllImport,
                singleFileOutputName = "Bindings.cs"
            };
            config.marshallingMappings["bgcs_sum"] = new FunctionMarshallingMapping
            {
                parameters =
                {
                    ["values"] = new MarshallingMapping
                    {
                        strategy = MarshallingStrategy.Span,
                        lengthParameter = "count",
                        ownership = BindingOwnership.Borrowed
                    }
                }
            };
            config.marshallingMappings["bgcs_name"] = new FunctionMarshallingMapping
            {
                @return = new MarshallingMapping { ownership = BindingOwnership.Borrowed }
            };
            config.functionMappings.Add(new FunctionMapping("bgcs_add", "BgcsAdd", null, [], [],
            [
                new ParameterMapping("left", "x", false),
                new ParameterMapping("right", null, false)
            ])
            {
                containerName = "MathApi"
            });
            config.functionMappings.Add(new FunctionMapping("bgcs_read", "BgcsRead", null, [], [],
            [
                new ParameterMapping("value", "result", true)
            ]));

            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output));
            string source = string.Join(Environment.NewLine,
                generator.lastResult!.outputFiles.Select(File.ReadAllText));
            Assert.Contains("public unsafe partial class MathApi", source);
            Assert.Contains("public static int BgcsAdd(int x, int right)", source);
            Assert.Contains("FriendlyApi.BgcsAddNative(x, right)", source);
            Assert.Contains("public static int BgcsNameLength(string name)", source);
            Assert.Contains("public static string? BgcsName()", source);
            Assert.Contains("public static void BgcsSum(ReadOnlySpan<int> values)", source);
            Assert.Contains("public static void BgcsFarSize(int size, int kind, int mode, int* data)", source);
            Assert.DoesNotContain("BgcsFarSize(int kind, int mode, ReadOnlySpan<int>", source);
            Assert.Contains("public static void BgcsRead(out int result)", source);
            Assert.Contains("internal static extern int BgcsAddNative", source);
            Assert.All(generator.lastResult.outputFiles, file =>
                Assert.DoesNotContain(CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetDiagnostics(),
                    diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            AssertCompiles(source);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void CSharpEmitter_OwnedStringWithoutCleanupCallable_ShouldFailClosed()
    {
        BindingModule module = new("OwnedApi", "BGCS.Tests.Generated", "owned", "host");
        module = module with { functions = [.. module.functions, new BindingFunction("bgcs_create_name", "BgcsCreateName", BindingFunctionKind.Free,
            new("const char *", "byte*", 1, true, IntPtr.Size),
            new(MarshallingStrategy.String, BindingOwnership.Owned, BindingStringEncoding.Utf8,
                requiresCleanup: true, cleanupFunction: "bgcs_free_name", nullTerminated: true))] };

        BindingDiagnostic diagnostic = Assert.Single(new CSharpEmitter().Validate(module));

        Assert.Equal(BindingDiagnosticCodes.C_CSHARPUNSUPPORTED, diagnostic.code);
        Assert.Contains("cleanup function 'bgcs_free_name'", diagnostic.message);
    }

    [Fact]
    public void Generate_StrictSafety_ShouldRequireAndPreserveCallbackLifetimeThreadingAndAsyncContracts()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-callback-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "callback.h");
        File.WriteAllText(header,
            "typedef void (*bgcs_callback)(int value);\n" +
            "void bgcs_register(bgcs_callback callback);\n" +
            "void bgcs_unregister(void);\n" +
            "void bgcs_complete(void);\n");
        try
        {
            CsCodeGeneratorConfig missing = CreateStrictConfig();
            CsCodeGenerator missingGenerator = new(missing);
            Assert.True(missingGenerator.Generate(header, Path.Combine(temp, "missing")));
            Assert.Contains(missingGenerator.lastResult!.diagnostics,
                diagnostic => diagnostic.code == BindingDiagnosticCodes.C_CALLBACKLIFETIME);
            Assert.Contains(missingGenerator.lastResult.diagnostics,
                diagnostic => diagnostic.code == BindingDiagnosticCodes.C_CALLBACKTHREADING);

            CsCodeGeneratorConfig configured = CreateStrictConfig();
            configured.marshallingMappings["bgcs_register"] = new FunctionMarshallingMapping
            {
                parameters =
                {
                    ["callback"] = new MarshallingMapping
                    {
                        strategy = MarshallingStrategy.Callback,
                        ownership = BindingOwnership.Borrowed,
                        callbackLifetime = BindingCallbackLifetime.RetainedUntilCompletion,
                        callbackThreading = BindingCallbackThreading.AnyThread,
                        asyncCompletion = BindingAsyncCompletion.Callback,
                        completionFunction = "bgcs_complete",
                        unregisterFunction = "bgcs_unregister"
                    }
                }
            };
            CsCodeGenerator configuredGenerator = new(configured);
            Assert.True(configuredGenerator.Generate(header, Path.Combine(temp, "configured")));
            BindingFunction register = Assert.Single(configuredGenerator.lastResult!.module!.functions,
                function => function.nativeName == "bgcs_register");
            MarshallingPlan contract = register.parameters[0].marshalling;
            Assert.Equal(BindingCallbackLifetime.RetainedUntilCompletion, contract.callbackLifetime);
            Assert.Equal(BindingCallbackThreading.AnyThread, contract.callbackThreading);
            Assert.Equal(BindingAsyncCompletion.Callback, contract.asyncCompletion);
            Assert.DoesNotContain(configuredGenerator.lastResult.diagnostics,
                diagnostic => diagnostic.code is BindingDiagnosticCodes.C_CALLBACKLIFETIME or
                    BindingDiagnosticCodes.C_CALLBACKTHREADING or BindingDiagnosticCodes.C_ASYNCLIFETIME);
        }
        finally
        {
            Directory.Delete(temp, true);
        }

        static CsCodeGeneratorConfig CreateStrictConfig() => new()
        {
            apiName = "CallbackApi",
            @namespace = "BGCS.Tests.Generated",
            libName = "callback",
            parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
            importType = ImportType.DllImport,
            strictSafety = true,
            strictSafetySeverity = StrictSafetySeverity.Warning,
            singleFileOutputName = "Bindings.cs"
        };
    }

    [Fact]
    public void Generate_StrictSafety_ShouldAcceptExplicitAllocatorDeallocatorPair()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-allocator-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "allocator.h");
        File.WriteAllText(header, "char* bgcs_create_name(void);\nvoid bgcs_free_name(char* value);\n");
        try
        {
            CsCodeGeneratorConfig config = new()
            {
                apiName = "AllocatorApi",
                @namespace = "BGCS.Tests.Generated",
                libName = "allocator",
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                importType = ImportType.DllImport,
                strictSafety = true,
                strictSafetySeverity = StrictSafetySeverity.Error,
                singleFileOutputName = "Bindings.cs"
            };
            config.marshallingMappings["bgcs_create_name"] = new FunctionMarshallingMapping
            {
                @return = new MarshallingMapping
                {
                    strategy = MarshallingStrategy.String,
                    ownership = BindingOwnership.Owned,
                    encoding = BindingStringEncoding.Utf8,
                    requiresCleanup = true,
                    cleanupFunction = "bgcs_free_name",
                    allocatorKind = BindingAllocatorKind.NativeFunction,
                    allocatorFunction = "bgcs_create_name",
                    nullTerminated = true
                }
            };
            config.marshallingMappings["bgcs_free_name"] = new FunctionMarshallingMapping
            {
                parameters =
                {
                    ["value"] = new MarshallingMapping
                    {
                        strategy = MarshallingStrategy.Pointer,
                        ownership = BindingOwnership.Transferred
                    }
                }
            };

            CsCodeGenerator generator = new(config);
            Assert.True(generator.Generate(header, Path.Combine(temp, "out")),
                string.Join(Environment.NewLine, generator.lastResult?.diagnostics.Select(diagnostic => diagnostic.message) ?? []));
            BindingFunction create = Assert.Single(generator.lastResult!.module!.functions,
                function => function.nativeName == "bgcs_create_name");
            Assert.Equal(BindingAllocatorKind.NativeFunction, create.returnMarshalling.allocatorKind);
            Assert.DoesNotContain(generator.lastResult.diagnostics,
                diagnostic => diagnostic.code == BindingDiagnosticCodes.C_ALLOCATOR);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private static void AssertCompiles(params string[] sources)
    {
        string trustedAssemblies = Assert.IsType<string>(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"));
        HashSet<string> references = trustedAssemblies.Split(Path.PathSeparator)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        references.Add(typeof(BGCS.Runtime.Bool8).Assembly.Location);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "BGCS.Generated.IrFriendly",
            sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            references.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable));
        using MemoryStream assembly = new();
        var result = compilation.Emit(assembly);
        Assert.True(result.Success, string.Join(Environment.NewLine,
            result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
    }

    [Fact]
    public void SingleFileComposer_ShouldPreserveNullableContextForGeneratedCode()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-composer-nullable-" + Guid.NewGuid().ToString("N"));
        string output = Path.Combine(temp, "Bindings.cs");
        try
        {
            new SingleFileComposer().ComposeSources(
                [("input.cs", "#nullable enable\nnamespace BGCS.Tests.Generated { public sealed class Value { public object? Item { get; set; } } }")],
                output,
                "BGCS.Tests.Generated");
            string source = File.ReadAllText(output);

            Assert.Contains("#nullable enable", source);
            Assert.DoesNotContain(CSharpSyntaxTree.ParseText(source).GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void SingleFileComposer_ShouldAlignHeaderAndNestedSource()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-composer-format-" + Guid.NewGuid().ToString("N"));
        string output = Path.Combine(temp, "Bindings.cs");
        try
        {
            new SingleFileComposer().ComposeSources(
                [("input.cs", "namespace BGCS.Tests.Generated { public class Value { public int Get() { return 1; } } }")],
                output, "BGCS.Tests.Generated", "macos-arm64-darwin");
            string source = File.ReadAllText(output);

            Assert.Contains("//     This code was generated by BindGen-CS.\n" +
                "//     ABI reference target: macos-arm64-darwin\n" +
                "//     Changes will be replaced", source.Replace("\r\n", "\n", StringComparison.Ordinal));
            Assert.Matches(@"(?m)^    public class Value\r?$", source);
            Assert.Matches(@"(?m)^        public int Get\(\)\r?$", source);
            Assert.Matches(@"(?m)^            return 1;\r?$", source);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CSharpEmitter_ShouldFormatSingleAndSplitDocuments(bool singleFile)
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-emitter-format-" + Guid.NewGuid().ToString("N"));
        try
        {
            BindingModule module = new("Sample", "BGCS.Tests.Generated", "sample", "macos-arm64-darwin");
            BindingType type = new("sample_value", "SampleValue", BindingTypeKind.Structure, 4, 4);
            type = type with { fields = [.. type.fields, new BindingField("value", "Value", new BindingTypeReference("int", "int", 0, false, 4),
                0, 0, 0, [])] };
            module = module with { types = [.. module.types, type] };

            string path = Assert.Single(new CSharpEmitter().Emit(module,
                new EmissionContext(temp, singleFile, "Bindings.cs")));
            string source = File.ReadAllText(path);
            Assert.Matches(@"(?m)^    public partial struct SampleValue\r?$", source);
            Assert.Matches(@"(?m)^        public int Value;\r?$", source);
            Assert.DoesNotContain(CSharpSyntaxTree.ParseText(source).GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_IntermediateRepresentationBackend_ShouldEmitVoidRecordsAndAnonymousNestedTypes()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-ir-nested-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "api.h");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "typedef void bgcs_context;\n" +
            "typedef struct BgcsParent { struct { int value; } child; union { float x; int y; } data; union { int first; float second; }; } BgcsParent;\n" +
            "void bgcs_take(bgcs_context* context, BgcsParent* parent);");
        try
        {
            CsCodeGeneratorConfig config = new()
            {
                apiName = "IrNestedApi",
                @namespace = "BGCS.Tests.Generated",
                libName = "ir_nested",
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                importType = ImportType.DllImport,
                autoSquashTypedef = false,

                singleFileOutputName = "GeneratedBindings.cs"
            };

            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output));
            BindingModule module = generator.lastResult!.module!;
            BindingType context = Assert.Single(module.types,
                type => type.nativeName == "bgcs_context");
            Assert.Equal(BindingTypeKind.Structure, context.kind);
            BindingType parent = Assert.Single(module.types,
                type => type.kind == BindingTypeKind.Structure && type.nativeName == "BgcsParent");
            Assert.Equal(3, parent.nestedTypes.Count);
            Assert.All(parent.fields, field => Assert.False(string.IsNullOrWhiteSpace(field.managedName)));
            string source = string.Join(Environment.NewLine,
                generator.lastResult.outputFiles.Select(File.ReadAllText));
            Assert.Contains($"partial struct {context.managedName}", source);
            Assert.All(parent.nestedTypes, nested => Assert.Contains($"partial struct {nested.managedName}", source));
            Assert.All(parent.fields, field => Assert.Contains($"public {field.type.managedName} {field.managedName};", source));
            Assert.DoesNotContain($"using {context.managedName} = void", source);
            Assert.All(generator.lastResult.outputFiles, file =>
                Assert.DoesNotContain(CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetDiagnostics(),
                    diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_IntermediateRepresentationBackend_ShouldIgnoreInlineImplementationDetails()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-ir-inline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "api.h");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "static inline int bgcs_helper(int value) { return value + 1; }\nint bgcs_public(int value);");
        try
        {
            CsCodeGeneratorConfig config = new()
            {
                apiName = "IrInlineApi",
                @namespace = "BGCS.Tests.Generated",
                libName = "ir_inline",
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                importType = ImportType.DllImport,

                singleFileOutputName = "GeneratedBindings.cs"
            };

            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output));
            BindingGenerationResult<BindingModule> result = Assert.IsType<BindingGenerationResult<BindingModule>>(generator.lastResult);
            BindingFunction function = Assert.Single(result.module!.functions);
            Assert.Equal("bgcs_public", function.nativeName);
            Assert.Equal(BindingFunctionKind.Free, function.kind);
            string source = File.ReadAllText(Assert.Single(result.outputFiles));
            Assert.Contains("BgcsPublicNative", source);
            Assert.DoesNotContain("BgcsHelper", source);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_IntermediateRepresentationBackend_ShouldEmitConfiguredSingleFile()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-ir-backend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "api.h");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "#define BGCS_LIMIT 8\ntypedef struct BgcsPoint { int x; int y; } BgcsPoint;\nint bgcs_add(int left, int right);");
        try
        {
            CsCodeGeneratorConfig config = new()
            {
                apiName = "IrBackendApi",
                @namespace = "BGCS.Tests.Generated",
                libName = "ir_backend",
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                importType = ImportType.DllImport,

                mergeGeneratedFilesToSingleFile = true,
                singleFileOutputName = "GeneratedBindings.cs"
            };

            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output));
            BindingGenerationResult<BindingModule> result = Assert.IsType<BindingGenerationResult<BindingModule>>(generator.lastResult);
            string generated = Assert.Single(result.outputFiles);
            Assert.Equal("GeneratedBindings.cs", Path.GetFileName(generated));
            string source = File.ReadAllText(generated);
            Assert.Contains("public const int BGCS_LIMIT = 8;", source);
            Assert.Contains("public partial struct BgcsPoint", source);
            Assert.Contains("BgcsAddNative", source);
            Assert.Contains("EntryPoint = \"bgcs_add\"", source);
            Assert.DoesNotContain(CSharpSyntaxTree.ParseText(source).GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_IntermediateRepresentationBackendUnsupportedDeclaration_ShouldPreserveLastGoodOutput()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-ir-backend-reject-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "api.h");
        string output = Path.Combine(temp, "out");
        Directory.CreateDirectory(output);
        string sentinel = Path.Combine(output, "last-good.txt");
        File.WriteAllText(sentinel, "last-good");
        File.WriteAllText(header, "void bgcs_log(const char* format, ...);");
        try
        {
            CsCodeGeneratorConfig config = new()
            {
                apiName = "IrBackendApi",
                @namespace = "BGCS.Tests.Generated",
                libName = "ir_backend",
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,

            };

            CsCodeGenerator generator = new(config);

            Assert.False(generator.Generate(header, output));
            BindingGenerationResult<BindingModule> result = Assert.IsType<BindingGenerationResult<BindingModule>>(generator.lastResult);
            Assert.False(result.success);
            Assert.Contains(result.diagnostics, diagnostic =>
                diagnostic.code == BindingDiagnosticCodes.C_CSHARPUNSUPPORTED &&
                diagnostic.message.Contains("variadic", StringComparison.Ordinal));
            Assert.Equal("last-good", File.ReadAllText(sentinel));
            Assert.Single(Directory.GetFiles(output));
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Theory]
    [InlineData(BindingImportMode.DllImport, "[DllImport")]
    [InlineData(BindingImportMode.LibraryImport, "[LibraryImport")]
    [InlineData(BindingImportMode.FunctionTable, "candidate.LoadRequired(0, \"bgcs_add\")")]
    public void CSharpEmitter_ImportModes_ShouldBeRepresentedByIr(BindingImportMode mode, string expected)
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-emitter-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            BindingModule module = new("NativeApi", "BGCS.Tests.Generated", "native", "host")
            {
                importMode = mode,
                useCustomContext = mode == BindingImportMode.FunctionTable
            };
            BindingFunction function = new("bgcs_add", "BgcsAdd", BindingFunctionKind.Free,
                new("int", "int", 0, false, 4),
                new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed));
            function = function with { parameters = [.. function.parameters, new("left", "left", new("int", "int", 0, false, 4), BindingDirection.In,
                new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed))] };
            if (mode == BindingImportMode.FunctionTable)
            {
                function = function with { functionTableIndex = 0 };
                module = module with { functionTableEntries = [.. module.functionTableEntries, new(0, function.nativeName)] };
            }
            module = module with { functions = [.. module.functions, function] };

            string emitted = Assert.Single(new CSharpEmitter().Emit(module, new(temp, true, "Bindings.cs")));
            string source = File.ReadAllText(emitted);
            Assert.Contains(expected, source);
            Assert.DoesNotContain(CSharpSyntaxTree.ParseText(source).GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    /// <summary>
    /// Verifies that every import strategy preserves the same pointer carrier and typed handle API.
    /// </summary>
    /// <param name="targetAbi">
    /// The native parsing target recorded in the binding module.
    /// </param>
    /// <param name="importMode">
    /// The import strategy used for native calls.
    /// </param>
    [Theory]
    [InlineData("emscripten-wasm32-emscripten", BindingImportMode.DllImport)]
    [InlineData("emscripten-wasm32-emscripten", BindingImportMode.LibraryImport)]
    [InlineData("emscripten-wasm32-emscripten", BindingImportMode.FunctionTable)]
    [InlineData("windows-x64-msvc", BindingImportMode.DllImport)]
    [InlineData("windows-x64-msvc", BindingImportMode.LibraryImport)]
    [InlineData("windows-x64-msvc", BindingImportMode.FunctionTable)]
    [InlineData("macos-arm64-darwin", BindingImportMode.DllImport)]
    [InlineData("macos-arm64-darwin", BindingImportMode.LibraryImport)]
    [InlineData("macos-arm64-darwin", BindingImportMode.FunctionTable)]
    public void CSharpEmitter_OpaqueHandle_UsesPointerSizedNativeCarrier(
        string targetAbi,
        BindingImportMode importMode
    ) {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-wasm-handle-" + Guid.NewGuid().ToString("N"));
        try
        {
            BindingModule module = new("WindowApi", "BGCS.Tests.Generated", "window", targetAbi)
            {
                importMode = importMode,
                useCustomContext = importMode == BindingImportMode.FunctionTable
            };
            module = module with { types = [.. module.types, new BindingType("NativeWindow", "NativeWindow", BindingTypeKind.OpaqueHandle, 4, 4)] };
            BindingFunction function = new("window_next", "WindowNext", BindingFunctionKind.Free,
                new("NativeWindow", "NativeWindow", 0, false, 4),
                new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed));
            function = function with { parameters = [.. function.parameters, new BindingParameter("window", "window",
                new("NativeWindow", "NativeWindow", 0, false, 4), BindingDirection.In,
                new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed))] };
            if (importMode == BindingImportMode.FunctionTable)
            {
                function = function with { functionTableIndex = 0 };
                module = module with { functionTableEntries = [.. module.functionTableEntries, new(0, function.nativeName)] };
            }
            module = module with { functions = [.. module.functions, function] };

            string emitted = Assert.Single(new CSharpEmitter().Emit(module, new(temp, true, "Bindings.cs")));
            string source = File.ReadAllText(emitted);

            Assert.Contains("nint WindowNextInterop(nint window)", source);
            Assert.Contains("NativeWindow WindowNextNative(NativeWindow window)", source);
            Assert.Contains("new NativeWindow(WindowNextInterop(window.Handle))", source);
            if (importMode == BindingImportMode.FunctionTable)
                Assert.Contains("delegate* unmanaged[Cdecl]<nint, nint>", source);
            if (importMode == BindingImportMode.LibraryImport)
                AssertCompilesWithSdk(temp);
            else
                AssertCompiles(source);
            Assert.DoesNotContain(CSharpSyntaxTree.ParseText(source).GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Theory]
    [InlineData(BindingImportMode.DllImport, "long")]
    [InlineData(BindingImportMode.LibraryImport, "long")]
    [InlineData(BindingImportMode.FunctionTable, "long")]
    [InlineData(BindingImportMode.DllImport, "ulong")]
    [InlineData(BindingImportMode.LibraryImport, "ulong")]
    [InlineData(BindingImportMode.FunctionTable, "ulong")]
    public void CSharpEmitter_EnumCallsUseIntegralCarriersAndPreservePointerTypes(
        BindingImportMode importMode,
        string carrier
    ) {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-enum-carrier-" + Guid.NewGuid().ToString("N"));
        try
        {
            BindingTypeReference value = new("Flags", "Flags", 0, false, 8);
            BindingTypeReference pointer = new("Flags*", "Flags*", 1, false, 8);
            MarshallingPlan marshalling = new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed);
            BindingFunction function = new("transform", "Transform", BindingFunctionKind.Free, value, marshalling)
            {
                parameters = [
                    new("value", "value", value, BindingDirection.In, marshalling),
                    new("output", "output", pointer, BindingDirection.In, marshalling)
                ],
                functionTableIndex = importMode == BindingImportMode.FunctionTable ? 0 : null
            };
            BindingModule module = new("Api", "Fixture", "api", "emscripten-wasm32-emscripten")
            {
                importMode = importMode,
                useCustomContext = importMode == BindingImportMode.FunctionTable,
                types = [new BindingType("Flags", "Flags", BindingTypeKind.Enumeration, 8, 8)
                {
                    underlyingType = new(carrier, carrier, 0, false, 8)
                }],
                functions = [function],
                functionTableEntries = importMode == BindingImportMode.FunctionTable ? [new(0, "transform")] : []
            };
            string path = Assert.Single(new CSharpEmitter().Emit(module, new(directory, true, "Bindings.cs")));
            string source = File.ReadAllText(path);

            Assert.Contains($"{carrier} TransformInterop({carrier} value, Flags* output)", source);
            Assert.Contains("Flags TransformNative(Flags value, Flags* output)", source);
            Assert.Contains($"(Flags)TransformInterop(({carrier})value, output)", source);
            if (importMode == BindingImportMode.FunctionTable)
                Assert.Contains($"delegate* unmanaged[Cdecl]<{carrier}, Flags*, {carrier}>", source);
            if (importMode == BindingImportMode.LibraryImport)
                AssertCompilesWithSdk(directory);
            else
                AssertCompiles(source);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("BGCS.Runtime", true)]
    [InlineData("Custom.Runtime", true)]
    [InlineData("BGCS.Runtime", false)]
    [InlineData("Custom.Runtime", false)]
    public void CSharpEmitter_FunctionTableNamespaceDoesNotShadowRuntimeTypes(
        string runtimeNamespace,
        bool customContext
    ) {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-table-namespace-" + Guid.NewGuid().ToString("N"));
        try
        {
            BindingModule module = new("NativeApi", "Example.FunctionTable", "native", "host")
            {
                importMode = BindingImportMode.FunctionTable,
                useCustomContext = customContext
            };
            BindingFunction function = new("fixture_value", "FixtureValue", BindingFunctionKind.Free,
                new("int", "int", 0, false, 4), new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed))
            {
                functionTableIndex = 0
            };
            module = module with { functions = [.. module.functions, function] };
            module = module with { functionTableEntries = [.. module.functionTableEntries, new(0, function.nativeName)] };
            EmissionContext context = new(directory, true, "Bindings.cs", runtimeNamespace);
            string path = Assert.Single(new CSharpEmitter().Emit(module, context));
            string runtime = Assert.Single(new RuntimeEmitter().Emit(module, context));
            AssertCompiles(File.ReadAllText(path), File.ReadAllText(runtime));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private static void AssertCompilesWithSdk(string directory)
    {
        XElement project = new("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup",
                new XElement("TargetFramework", "net9.0"),
                new XElement("AllowUnsafeBlocks", true),
                new XElement("ImplicitUsings", "disable"),
                new XElement("Nullable", "enable")),
            new XElement("ItemGroup",
                new XElement("Reference", new XAttribute("Include", "BGCS.Runtime"),
                    new XElement("HintPath", typeof(BGCS.Runtime.Bool8).Assembly.Location))));
        string path = Path.Combine(directory, "Generated.csproj");
        File.WriteAllText(path, project.ToString());
        string host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        ProcessStartInfo start = new(host)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "build", path, "--nologo", "-m:1", "-nodeReuse:false" })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        bool completed = process.WaitForExit(60000);
        if (!completed)
            process.Kill(entireProcessTree: true);
        process.WaitForExit();
        Task.WaitAll(output, error);
        Assert.True(completed && process.ExitCode == 0, output.Result + error.Result);
    }

    [Theory]
    [InlineData(BindingImportMode.DllImport, "[DllImport(LibName")]
    [InlineData(BindingImportMode.LibraryImport, "[LibraryImport(LibName")]
    public void CSharpEmitter_ExternalLibraryNameConstant_ShouldRemainADeclarativeExtensionPoint(
        BindingImportMode mode,
        string expectedImport)
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-emitter-library-name-" + Guid.NewGuid().ToString("N"));
        try
        {
            BindingModule module = new("NativeApi", "BGCS.Tests.Generated", "native", "host")
            {
                importMode = mode,
                emitLibraryNameConstant = false
            };
            module = module with { functions = [.. module.functions, new BindingFunction("bgcs_add", "BgcsAdd", BindingFunctionKind.Free,
                new("int", "int", 0, false, 4),
                new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed))] };

            string emitted = Assert.Single(new CSharpEmitter().Emit(module, new(temp, true, "Bindings.cs")));
            string source = File.ReadAllText(emitted);

            Assert.Contains(expectedImport, source, StringComparison.Ordinal);
            Assert.DoesNotContain("internal const string LibName", source, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void CSharpEmitter_Bitfields_ShouldUseExplicitStorageAndSignedAccessors()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-emitter-bitfields-" + Guid.NewGuid().ToString("N"));
        try
        {
            BindingModule module = new("BitsApi", "BGCS.Tests.Generated", "bits", "host");
            BindingType type = new("Flags", "Flags", BindingTypeKind.Structure, 4, 4);
            type = type with { fields = [.. type.fields, new BindingField("mode", "Mode", new("int", "int", 0, false, 4),
                0, 0, 3, [], true, true)] };
            type = type with { fields = [.. type.fields, new BindingField("enabled", "Enabled", new("unsigned int", "uint", 0, false, 4),
                0, 3, 1, [], true)] };
            module = module with { types = [.. module.types, type] };

            string emitted = Assert.Single(new CSharpEmitter().Emit(module, new(temp, true, "Bindings.cs")));
            string source = File.ReadAllText(emitted);
            Assert.Contains("LayoutKind.Explicit, Size = 4", source);
            Assert.Contains("Bitfield.GetSigned(MemoryMarshal.CreateReadOnlySpan(ref RawBits0_0, 1), 0, 3)", source);
            Assert.Contains("Bitfield.Get(MemoryMarshal.CreateReadOnlySpan(ref RawBits0_0, 1), 3, 1)", source);
            AssertCompiles(source);
            Assert.DoesNotContain(CSharpSyntaxTree.ParseText(source).GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void CSharpEmitter_PackedStructure_ShouldPreserveNativeSizeAndAlignment()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-emitter-packed-" + Guid.NewGuid().ToString("N"));
        try
        {
            BindingModule module = new("PackedApi", "BGCS.Tests.Generated", "packed", "host");
            BindingType type = new("Packed", "Packed", BindingTypeKind.Structure, 5, 1);
            type = type with { fields = [.. type.fields, new BindingField("tag", "Tag", new("char", "byte", 0, false, 1), 0, 0, 0, [])] };
            type = type with { fields = [.. type.fields, new BindingField("value", "Value", new("int", "int", 0, false, 4), 1, 8, 0, [])] };
            module = module with { types = [.. module.types, type] };

            string emitted = Assert.Single(new CSharpEmitter().Emit(module, new(temp, true, "Bindings.cs")));
            string source = File.ReadAllText(emitted);
            Assert.Contains("LayoutKind.Sequential, Size = 5, Pack = 1", source, StringComparison.Ordinal);
            Assert.DoesNotContain(CSharpSyntaxTree.ParseText(source).GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Theory]
    [InlineData("windows-x64-msvc")]
    [InlineData("linux-x64-gnu")]
    public void Generate_MixedAndPackedBitfields_PreservesNativeByteBounds(string targetId)
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-mixed-bitfields-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "bits.h");
        File.WriteAllText(header,
            "typedef enum Kind { KIND_ZERO = 0, KIND_ONE = 1 } Kind;\n" +
            "typedef struct Mixed { unsigned int count:8; Kind kind:8; unsigned int offset:16; } Mixed;\n" +
            "#pragma pack(push, 1)\n" +
            "typedef struct Packed { unsigned int value:7; } Packed;\n" +
            "typedef struct BooleanBits { _Bool enabled:1; } BooleanBits;\n" +
            "#pragma pack(pop)\n");
        try
        {
            CsCodeGeneratorConfig config = new()
            {
                apiName = "BitApi",
                @namespace = "BGCS.Tests.Generated",
                libName = "bits",
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                targetId = targetId,
                singleFileOutputName = "Bindings.cs"
            };
            CsCodeGenerator generator = new(config);
            Assert.True(generator.Generate(header, Path.Combine(temp, "out")),
                string.Join(Environment.NewLine, generator.lastResult?.diagnostics.Select(diagnostic => diagnostic.message) ?? []));
            BindingType mixed = Assert.Single(generator.lastResult!.module!.types, type => type.nativeName == "Mixed");
            Assert.Equal(4, mixed.size);
            Assert.Equal(new long[] { 0, 8, 16 }, mixed.fields.Select(field => field.bitOffset));
            string[] sources = generator.lastResult.outputFiles.Select(File.ReadAllText).ToArray();
            AssertCompiles(sources);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void CSharpEmitter_EmptyUnion_ShouldEmitExplicitlyPositionedOpaqueStorage()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-emitter-empty-union-" + Guid.NewGuid().ToString("N"));
        try
        {
            BindingModule module = new("UnionApi", "BGCS.Tests.Generated", "union", "host");
            module = module with { types = [.. module.types, new BindingType("NativeUnion", "NativeUnion", BindingTypeKind.Union, 8, 8)] };

            string emitted = Assert.Single(new CSharpEmitter().Emit(module, new(temp, true, "Bindings.cs")));
            string source = File.ReadAllText(emitted);
            Assert.Contains("#nullable enable", source);
            Assert.Contains("LayoutKind.Explicit, Size = 8, Pack = 8", source);
            Assert.Contains("[FieldOffset(0)]", source);
            Assert.DoesNotContain(CSharpSyntaxTree.ParseText(source).GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void CSharpEmitter_UnsupportedCallableSemantics_ShouldFailBeforeWritingOutput()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-emitter-validation-" + Guid.NewGuid().ToString("N"));
        BindingModule module = new("UnsafeApi", "BGCS.Tests.Generated", "unsafe", "host");
        BindingType type = new("Flags", "Flags", BindingTypeKind.Structure, 4, 4);
        type = type with { fields = [.. type.fields, new BindingField("enabled", "Enabled", new("unsigned int", "uint", 0, false, 4),
            0, 0, 1, [], true)] };
        module = module with { types = [.. module.types, type] };
        module = module with { functions = [.. module.functions, new BindingFunction("log", "Log", BindingFunctionKind.Free,
            new("void", "void", 0, false, 0),
            new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed)) { isVariadic = true }] };

        BindingEmissionException exception = Assert.Throws<BindingEmissionException>(() =>
            new CSharpEmitter().Emit(module, new(temp, true, "Bindings.cs")));

        Assert.Single(exception.diagnostics);
        Assert.All(exception.diagnostics, diagnostic => Assert.Equal(BindingDiagnosticCodes.C_CSHARPUNSUPPORTED, diagnostic.code));
        Assert.False(Directory.Exists(temp));
    }

    [Fact]
    public void CSharpEmitter_OpaqueStorageByValue_ShouldFailBeforeWritingOutput()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-emitter-opaque-value-" + Guid.NewGuid().ToString("N"));
        BindingModule module = new("UnsafeApi", "BGCS.Tests.Generated", "unsafe", "host");
        module = module with { types = [.. module.types, new BindingType("NativeHidden", "NativeHidden", BindingTypeKind.Structure, 16, 8)
        {
            isOpaqueStorage = true
        }] };
        module = module with { functions = [.. module.functions, new BindingFunction("consume", "Consume", BindingFunctionKind.Free,
            new("void", "void", 0, false, 0),
            new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed))
        {
            parameters =
            [
                new BindingParameter("value", "value", new("NativeHidden", "NativeHidden", 0, false, 16),
                    BindingDirection.In, new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed))
            ]
        }] };

        BindingEmissionException exception = Assert.Throws<BindingEmissionException>(() =>
            new CSharpEmitter().Emit(module, new(temp, true, "Bindings.cs")));

        BindingDiagnostic diagnostic = Assert.Single(exception.diagnostics);
        Assert.Equal(BindingDiagnosticCodes.C_CSHARPUNSUPPORTED, diagnostic.code);
        Assert.Contains("opaque storage type 'NativeHidden' by value", diagnostic.message);
        Assert.False(Directory.Exists(temp));
    }

    [Fact]
    public void Generate_ExternalManagedCarrier_WithMatchingLayout_ShouldCompileAndRemainAuditable()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-external-carrier-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "external.h");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "typedef struct NativeVector { float x; float y; } NativeVector; " +
            "NativeVector vector_add(NativeVector left, NativeVector right);");
        try
        {
            CsCodeGeneratorConfig config = CreateExternalVectorConfig();
            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output));
            string source = File.ReadAllText(Assert.Single(generator.lastResult!.outputFiles,
                file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains("Vector2 left", source, StringComparison.Ordinal);
            Assert.DoesNotContain("struct Vector2", source, StringComparison.Ordinal);
            Assert.Contains(generator.lastResult.diagnostics, diagnostic =>
                diagnostic.code == BindingDiagnosticCodes.C_EXTERNALTYPE &&
                diagnostic.severity == BindingDiagnosticSeverity.Warning);
            BindingExternalTypeContract contract = Assert.Single(generator.lastResult.module!.externalTypes);
            Assert.Equal("Vector2", contract.managedType);
            Assert.True(contract.allowsByValue);
            Assert.False(contract.layoutValidationBypassed);
            AssertCompiles(source);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_ExternalManagedCarrier_RejectsMismatchUnlessPreciselyBypassed()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-external-carrier-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "external.h");
        File.WriteAllText(header,
            "typedef struct NativeVector { float x; float y; } NativeVector; " +
            "NativeVector vector_identity(NativeVector value);");
        try
        {
            CsCodeGeneratorConfig rejected = CreateExternalVectorConfig();
            rejected.externalTypeContracts[0].size = 16;
            CsCodeGenerator rejectedGenerator = new(rejected);
            Assert.False(rejectedGenerator.Generate(header, Path.Combine(temp, "rejected")));
            Assert.Contains(rejectedGenerator.lastResult!.diagnostics, diagnostic =>
                diagnostic.code == BindingDiagnosticCodes.C_EXTERNALTYPE &&
                diagnostic.severity == BindingDiagnosticSeverity.Error);

            CsCodeGeneratorConfig bypassed = CreateExternalVectorConfig();
            bypassed.externalTypeContracts[0].size = 16;
            bypassed.externalTypeContracts[0].byValuePolicy = ExternalTypeByValuePolicy.BypassLayoutValidation;
            CsCodeGenerator bypassedGenerator = new(bypassed);
            Assert.True(bypassedGenerator.Generate(header, Path.Combine(temp, "bypassed")));
            Assert.Contains(bypassedGenerator.lastResult!.diagnostics, diagnostic =>
                diagnostic.code == BindingDiagnosticCodes.C_EXTERNALTYPE &&
                diagnostic.severity == BindingDiagnosticSeverity.Warning);
            Assert.True(Assert.Single(bypassedGenerator.lastResult.module!.externalTypes).layoutValidationBypassed);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_ExternalGenericCarrierSelectors_ShouldCoverClosedTypeMappings()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-external-generic-carrier-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "external.h");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "typedef struct NativeVector_int { int size; int capacity; int* data; } NativeVector_int; " +
            "NativeVector_int vector_get(void);");
        try
        {
            CsCodeGeneratorConfig config = new()
            {
                apiName = "ExternalGenericApi",
                @namespace = "BGCS.Tests.Generated",
                libName = "external_generic",
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                importType = ImportType.DllImport,
                singleFileOutputName = "Bindings.cs"
            };
            config.typeMappings["NativeVector_int"] = "NativeVector<int>";
            config.ignoredTypes.Add("NativeVector_int");
            config.ignoredTypedefs.Add("NativeVector_int");
            config.externalTypeContracts.Add(new()
            {
                nativeTypes = ["NativeVector_*"],
                managedTypes = ["NativeVector<*>"],
                size = 16,
                alignment = 8,
                byValuePolicy = ExternalTypeByValuePolicy.RequireLayoutMatch
            });
            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output),
                string.Join(Environment.NewLine, generator.lastResult?.diagnostics.Select(diagnostic => diagnostic.message) ?? []));
            string source = File.ReadAllText(Assert.Single(generator.lastResult!.outputFiles,
                file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains("NativeVector<int>", source, StringComparison.Ordinal);
            Assert.DoesNotContain("struct NativeVector<int>", source, StringComparison.Ordinal);
            BindingExternalTypeContract contract = Assert.Single(generator.lastResult.module!.externalTypes);
            Assert.Equal("NativeVector<int>", contract.managedType);
            AssertCompiles(source + Environment.NewLine +
                "namespace BGCS.Tests.Generated { public unsafe struct NativeVector<T> where T : unmanaged " +
                "{ public int Size; public int Capacity; public T* Data; } }");
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_IrPresentation_ShouldDistinguishFactoriesFromInstanceMembersAndHonorDelegateSwitch()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-ir-member-presentation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "members.h");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "typedef struct NativeThing { unsigned char data[1]; void (*notify)(int); } NativeThing; " +
            "NativeThing* NativeThing_NativeThing(int value); " +
            "void NativeThing_Reset(NativeThing* self); " +
            "void NativeThing_Resize(NativeThing* self, int size); " +
            "int NativeThing_SetEnabled(NativeThing* self, _Bool* enabled); " +
            "int NativeBegin(const char* label, unsigned char* p_open, unsigned int flags); " +
            "int NativeFormat(const char* label, const char* format);");
        try
        {
            CsCodeGeneratorConfig config = new()
            {
                apiName = "NativeApi",
                @namespace = "BGCS.Tests.Generated",
                libName = "native",
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                importType = ImportType.DllImport,
                singleFileOutputName = "Bindings.cs",
                generateDelegates = false,
                generateExtensions = false,
                wrapPointersAsHandle = true,
                memberNamingConvention = NamingConvention.Unknown,
                // This fixture tests presentation rather than native lifetime semantics.
                strictSafetySeverity = StrictSafetySeverity.Warning
            };
            config.functionMappings.Add(new("NativeThing_NativeThing", "NativeThing", null, [], []));
            config.functionMappings.Add(new("NativeThing_Reset", "Reset", null, [], []));
            config.functionMappings.Add(new("NativeThing_Resize", "Resize", null, [], []));
            config.functionMappings.Add(new("NativeThing_SetEnabled", "SetEnabled", null, [], []));
            config.functionMappings.Add(new("NativeBegin", "NativeBegin", null,
                new() { ["p_open"] = "NULL", ["flags"] = "0" }, []));
            config.functionMappings.Add(new("NativeFormat", "NativeFormat", null,
                new() { ["format"] = "\"%.3f\"" }, []));
            config.knownMemberFunctions["NativeThing"] =
                ["NativeThing_NativeThing", "NativeThing_Reset", "NativeThing_Resize", "NativeThing_SetEnabled"];
            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output),
                string.Join(Environment.NewLine, generator.lastResult?.diagnostics.Select(diagnostic => diagnostic.message) ?? []));
            BindingFunction factory = Assert.Single(generator.lastResult!.module!.functions,
                function => function.nativeName == "NativeThing_NativeThing");
            BindingFunction reset = Assert.Single(generator.lastResult.module.functions,
                function => function.nativeName == "NativeThing_Reset");
            Assert.Equal(BindingManagedFunctionKind.Static, factory.managedKind);
            Assert.Equal(BindingManagedFunctionKind.Instance, reset.managedKind);
            Assert.Empty(generator.lastResult.module.delegates);
            string[] generatedSources = generator.lastResult.outputFiles
                .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText)
                .Where(text => !text.Contains("namespace BGCS.Runtime", StringComparison.Ordinal))
                .ToArray();
            string source = string.Join(Environment.NewLine, generatedSources);
            Assert.Contains("public static NativeThingPtr NativeThing(int value)", source, StringComparison.Ordinal);
            Assert.Contains("public void Reset()", source, StringComparison.Ordinal);
            Assert.Contains("NativeApi.Reset(this);", source, StringComparison.Ordinal);
            Assert.Contains("public void Resize(int size)", source, StringComparison.Ordinal);
            Assert.Contains("NativeApi.Resize(this, size);", source, StringComparison.Ordinal);
            Assert.Contains("public int SetEnabled(ref bool enabled)", source, StringComparison.Ordinal);
            Assert.Contains("this.data = data[0]", source, StringComparison.Ordinal);
            Assert.Contains("Handle->data, 1", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Handle->data_0", source, StringComparison.Ordinal);
            Assert.DoesNotContain("delegate void Notify", source, StringComparison.Ordinal);
            Assert.Contains("public static int NativeFormat(string label)", source, StringComparison.Ordinal);
            Assert.Contains("return NativeFormat(label, \"%.3f\")", source, StringComparison.Ordinal);
            Assert.Contains("public static int NativeBegin(string label, uint flags)", source, StringComparison.Ordinal);
            Assert.Contains("public static int NativeBegin(string label)", source, StringComparison.Ordinal);
            AssertCompiles(generatedSources);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_OpaqueRecordsAndPointerFriendlyTransforms_ShouldUseFinalHandleSurface()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-ir-handle-presentation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "handles.h");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            "typedef struct NativeWindowS NativeWindow; " +
            "typedef struct NativeEngine { int state; } NativeEngine; " +
            "NativeWindow* Native_CreateWindow(const char* title); " +
            "void Native_DestroyWindow(NativeWindow* window); " +
            "NativeWindow** Native_GetWindows(int* count); " +
            "NativeEngine* Native_GetEngine(void); " +
            "int Native_Open(NativeEngine* engine, const char* path);");
        try
        {
            CsCodeGeneratorConfig config = new()
            {
                apiName = "NativeApi",
                @namespace = "BGCS.Tests.Generated",
                libName = "native",
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                importType = ImportType.DllImport,
                singleFileOutputName = "Bindings.cs",
                generateExtensions = false,
                wrapPointersAsHandle = true
            };
            config.marshallingMappings["Native_CreateWindow"] = new FunctionMarshallingMapping
            {
                @return = new MarshallingMapping
                {
                    ownership = BindingOwnership.Owned,
                    allocatorKind = BindingAllocatorKind.NativeFunction,
                    allocatorFunction = "Native_CreateWindow",
                    cleanupFunction = "Native_DestroyWindow"
                }
            };
            config.marshallingMappings["Native_GetWindows"] = new FunctionMarshallingMapping
            {
                @return = new MarshallingMapping { ownership = BindingOwnership.Borrowed }
            };
            config.marshallingMappings["Native_GetEngine"] = new FunctionMarshallingMapping
            {
                @return = new MarshallingMapping { ownership = BindingOwnership.Borrowed }
            };
            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output),
                string.Join(Environment.NewLine, generator.lastResult?.diagnostics.Select(diagnostic => diagnostic.message) ?? []));
            BindingType window = Assert.Single(generator.lastResult!.module!.types,
                type => type.nativeName.Contains("NativeWindow", StringComparison.Ordinal) &&
                    type.kind == BindingTypeKind.OpaqueHandle);
            Assert.Equal("NativeWindow", window.managedName);
            BindingFunction create = Assert.Single(generator.lastResult.module.functions,
                function => function.nativeName == "Native_CreateWindow");
            Assert.Equal("NativeWindow", create.returnType.managedName);
            Assert.Equal(1, create.returnType.pointerDepth);
            BindingFunction getWindows = Assert.Single(generator.lastResult.module.functions,
                function => function.nativeName == "Native_GetWindows");
            Assert.Equal("NativeWindow*", getWindows.returnType.managedName);
            Assert.Equal(2, getWindows.returnType.pointerDepth);

            string[] generatedSources = generator.lastResult.outputFiles
                .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText)
                .Where(text => !text.Contains("namespace BGCS.Runtime", StringComparison.Ordinal))
                .ToArray();
            string source = string.Join(Environment.NewLine, generatedSources);
            Assert.Contains("public static NativeWindow NativeCreateWindow(string title)", source, StringComparison.Ordinal);
            Assert.Contains("public static void NativeDestroyWindow(NativeWindow window)", source, StringComparison.Ordinal);
            Assert.Contains("public static NativeEnginePtr NativeGetEngine()", source, StringComparison.Ordinal);
            Assert.Contains("public static int NativeOpen(NativeEnginePtr engine, string path)", source, StringComparison.Ordinal);
            AssertCompiles(generatedSources);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Generate_StrictSafetyError_ShouldRejectAndPreserveLastGoodOutput()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-strict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "strict.h");
        string output = Path.Combine(temp, "out");
        Directory.CreateDirectory(output);
        string sentinel = Path.Combine(output, "last-good.txt");
        File.WriteAllText(sentinel, "last-good");
        File.WriteAllText(header, "void* get_context(void);");
        try
        {
            CsCodeGeneratorConfig config = new()
            {
                @namespace = "Strict.Generated",
                apiName = "StrictApi",
                libName = "strict",
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                strictSafetySeverity = StrictSafetySeverity.Error
            };
            CsCodeGenerator generator = new(config);

            Assert.False(generator.Generate(header, output));
            Assert.False(generator.lastResult!.success);
            Assert.Contains(generator.lastResult.diagnostics, diagnostic =>
                diagnostic.code == "BGCS-SAFETY-OWNERSHIP" && diagnostic.severity == BindingDiagnosticSeverity.Error);
            Assert.Equal("last-good", File.ReadAllText(sentinel));
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    private static CsCodeGeneratorConfig CreateExternalVectorConfig()
    {
        CsCodeGeneratorConfig config = new()
        {
            apiName = "ExternalApi",
            @namespace = "BGCS.Tests.Generated",
            libName = "external",
            parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
            importType = ImportType.DllImport,
            singleFileOutputName = "Bindings.cs"
        };
        config.typeMappings["NativeVector"] = "Vector2";
        config.ignoredTypes.Add("NativeVector");
        config.ignoredTypedefs.Add("NativeVector");
        config.usings.Add("System.Numerics");
        config.externalTypeContracts.Add(new()
        {
            nativeTypes = ["NativeVector"],
            managedTypes = ["Vector2"],
            size = 8,
            alignment = 4,
            byValuePolicy = ExternalTypeByValuePolicy.RequireLayoutMatch
        });
        return config;
    }

    [Fact]
    public void Generate_ShouldProduceAbiAwareSharedIntermediateRepresentation()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-ir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string header = Path.Combine(temp, "api.h");
        string output = Path.Combine(temp, "out");
        File.WriteAllText(header,
            """
            #define BGCS_LIMIT 42
            #define BGCS_LABEL "bgcs"

            typedef unsigned int BgcsId;
            typedef void (*BgcsCallback)(int value);

            typedef struct BgcsItem
            {
                int id;
                float values[4];
            } BgcsItem;

            int bgcs_get_items(BgcsItem* output, int capacity, int* actual_count);
            const char* bgcs_create_name(void);
            void bgcs_free_name(const char* value);
            void* bgcs_get_context(void);
            """);
        CsCodeGeneratorConfig config = new()
        {
            apiName = "IrApi",
            @namespace = "BGCS.Tests.Generated",
            libName = "ir",
            importType = ImportType.DllImport,
            generateExtensions = false,
            autoSquashTypedef = false
        };
        config.marshallingMappings["bgcs_get_items"] = new FunctionMarshallingMapping
        {
            parameters =
            {
                ["output"] = new MarshallingMapping
                {
                    strategy = MarshallingStrategy.Span,
                    ownership = BindingOwnership.CallerAllocated,
                    lengthParameter = "actual_count",
                    capacityParameter = "capacity",
                    writtenCountParameter = "actual_count"
                }
            }
        };
        config.marshallingMappings["bgcs_create_name"] = new FunctionMarshallingMapping
        {
            @return = new MarshallingMapping
            {
                strategy = MarshallingStrategy.String,
                ownership = BindingOwnership.Owned,
                encoding = BindingStringEncoding.Utf8,
                cleanupFunction = "bgcs_free_name",
                requiresCleanup = true,
                nullTerminated = true
            }
        };

        try
        {
            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output));

            BindingGenerationResult<BindingModule> result = Assert.IsType<BindingGenerationResult<BindingModule>>(generator.lastResult);
            Assert.True(result.success);
            BindingConstant limit = Assert.Single(result.module!.constants, constant => constant.nativeName == "BGCS_LIMIT");
            Assert.Equal("int", limit.managedType);
            Assert.Equal("42", limit.value);
            Assert.Single(result.module.constants, constant => constant.nativeName == "BGCS_LABEL");
            BindingType alias = Assert.Single(result.module.types, type => type.nativeName.Contains("BgcsId", StringComparison.Ordinal));
            Assert.Equal(BindingTypeKind.Alias, alias.kind);
            Assert.Equal("uint", alias.underlyingType!.managedName);
            BindingDelegate callback = Assert.Single(result.module.delegates, value => value.nativeName == "BgcsCallback");
            Assert.Equal("void", callback.returnType.managedName);
            Assert.Single(callback.parameters);
            BindingType item = Assert.Single(result.module.types, type => type.kind == BindingTypeKind.Structure &&
                type.nativeName.Contains("BgcsItem", StringComparison.Ordinal));
            Assert.Equal(BindingTypeKind.Structure, item.kind);
            Assert.Equal(20, item.size);
            BindingField values = Assert.Single(item.fields, field => field.nativeName == "values");
            Assert.Equal(new[] { 4 }, values.arrayDimensions);
            BindingFunction function = Assert.Single(result.module.functions, value => value.nativeName == "bgcs_get_items");
            BindingParameter outputParameter = function.parameters[0];
            Assert.Equal(MarshallingStrategy.Span, outputParameter.marshalling.strategy);
            Assert.Equal(BindingOwnership.CallerAllocated, outputParameter.marshalling.ownership);
            Assert.Equal("actual_count", outputParameter.marshalling.lengthParameter);
            Assert.Equal("capacity", outputParameter.marshalling.capacityParameter);
            Assert.Equal("actual_count", outputParameter.marshalling.writtenCountParameter);
            BindingFunction createName = Assert.Single(result.module.functions, value => value.nativeName == "bgcs_create_name");
            Assert.Equal(BindingOwnership.Owned, createName.returnMarshalling.ownership);
            Assert.Equal(BindingStringEncoding.Utf8, createName.returnMarshalling.stringEncoding);
            Assert.Equal("bgcs_free_name", createName.returnMarshalling.cleanupFunction);
            Assert.True(createName.returnMarshalling.requiresCleanup);
            Assert.True(createName.returnMarshalling.nullTerminated);
            BindingDiagnostic safety = Assert.Single(result.diagnostics, diagnostic => diagnostic.code == "BGCS-SAFETY-OWNERSHIP");
            Assert.Contains("MarshallingMappings.bgcs_get_context.Return.Ownership", safety.message);
            Assert.NotEmpty(result.outputFiles);

            string irOutput = Path.Combine(temp, "ir-output");
            string emittedFile = Assert.Single(new CSharpEmitter().Emit(result.module, new(irOutput, true, "Bindings.cs")));
            Diagnostic[] syntaxErrors = CSharpSyntaxTree.ParseText(File.ReadAllText(emittedFile)).GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.Empty(syntaxErrors);
            Assert.Contains("BgcsGetItemsNative", File.ReadAllText(emittedFile));
            Assert.Contains("public const int", File.ReadAllText(emittedFile));
            Assert.Contains("delegate void BgcsCallback", File.ReadAllText(emittedFile));
            Assert.DoesNotContain("partial struct uint", File.ReadAllText(emittedFile));
            Assert.Contains("using BgcsId = uint;", File.ReadAllText(emittedFile));
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }
}
