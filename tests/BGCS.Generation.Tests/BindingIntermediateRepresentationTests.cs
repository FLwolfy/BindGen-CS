using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Core.Mapping;
using BGCS.Emission;
using BGCS.Intermediate;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public class BindingIntermediateRepresentationTests
{
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
                ApiName = "FriendlyApi",
                Namespace = "BGCS.Tests.Generated",
                LibName = "friendly",
                ParserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                ImportType = ImportType.DllImport,
                SingleFileOutputName = "Bindings.cs"
            };
            config.MarshallingMappings["bgcs_sum"] = new FunctionMarshallingMapping
            {
                Parameters =
                {
                    ["values"] = new MarshallingMapping
                    {
                        Strategy = MarshallingStrategy.Span,
                        LengthParameter = "count",
                        Ownership = BindingOwnership.Borrowed
                    }
                }
            };
            config.FunctionMappings.Add(new FunctionMapping("bgcs_add", "BgcsAdd", null, [], [],
            [
                new ParameterMapping("left", "x", false),
                new ParameterMapping("right", null, false)
            ])
            {
                ContainerName = "MathApi"
            });
            config.FunctionMappings.Add(new FunctionMapping("bgcs_read", "BgcsRead", null, [], [],
            [
                new ParameterMapping("value", "result", true)
            ]));

            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output));
            string source = string.Join(Environment.NewLine,
                generator.LastResult!.OutputFiles.Select(File.ReadAllText));
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
            Assert.All(generator.LastResult.OutputFiles, file =>
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
        module.Functions.Add(new BindingFunction("bgcs_create_name", "BgcsCreateName", BindingFunctionKind.Free,
            new("const char *", "byte*", 1, true, IntPtr.Size),
            new(MarshallingStrategy.String, BindingOwnership.Owned, BindingStringEncoding.Utf8,
                RequiresCleanup: true, CleanupFunction: "bgcs_free_name", NullTerminated: true)));

        BindingDiagnostic diagnostic = Assert.Single(new CSharpEmitter().Validate(module));

        Assert.Equal(BindingDiagnosticCodes.CSharpUnsupported, diagnostic.Code);
        Assert.Contains("cleanup function 'bgcs_free_name'", diagnostic.Message);
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
            Assert.Contains(missingGenerator.LastResult!.Diagnostics,
                diagnostic => diagnostic.Code == BindingDiagnosticCodes.CallbackLifetime);
            Assert.Contains(missingGenerator.LastResult.Diagnostics,
                diagnostic => diagnostic.Code == BindingDiagnosticCodes.CallbackThreading);

            CsCodeGeneratorConfig configured = CreateStrictConfig();
            configured.MarshallingMappings["bgcs_register"] = new FunctionMarshallingMapping
            {
                Parameters =
                {
                    ["callback"] = new MarshallingMapping
                    {
                        Strategy = MarshallingStrategy.Callback,
                        Ownership = BindingOwnership.Borrowed,
                        CallbackLifetime = BindingCallbackLifetime.RetainedUntilCompletion,
                        CallbackThreading = BindingCallbackThreading.AnyThread,
                        AsyncCompletion = BindingAsyncCompletion.Callback,
                        CompletionFunction = "bgcs_complete",
                        UnregisterFunction = "bgcs_unregister"
                    }
                }
            };
            CsCodeGenerator configuredGenerator = new(configured);
            Assert.True(configuredGenerator.Generate(header, Path.Combine(temp, "configured")));
            BindingFunction register = Assert.Single(configuredGenerator.LastResult!.Module!.Functions,
                function => function.NativeName == "bgcs_register");
            MarshallingPlan contract = register.Parameters[0].Marshalling;
            Assert.Equal(BindingCallbackLifetime.RetainedUntilCompletion, contract.CallbackLifetime);
            Assert.Equal(BindingCallbackThreading.AnyThread, contract.CallbackThreading);
            Assert.Equal(BindingAsyncCompletion.Callback, contract.AsyncCompletion);
            Assert.DoesNotContain(configuredGenerator.LastResult.Diagnostics,
                diagnostic => diagnostic.Code is BindingDiagnosticCodes.CallbackLifetime or
                    BindingDiagnosticCodes.CallbackThreading or BindingDiagnosticCodes.AsyncLifetime);
        }
        finally
        {
            Directory.Delete(temp, true);
        }

        static CsCodeGeneratorConfig CreateStrictConfig() => new()
        {
            ApiName = "CallbackApi",
            Namespace = "BGCS.Tests.Generated",
            LibName = "callback",
            ParserKind = BGCS.CppAst.Parsing.CppParserKind.C,
            ImportType = ImportType.DllImport,
            StrictSafety = true,
            StrictSafetySeverity = StrictSafetySeverity.Warning,
            SingleFileOutputName = "Bindings.cs"
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
                ApiName = "AllocatorApi",
                Namespace = "BGCS.Tests.Generated",
                LibName = "allocator",
                ParserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                ImportType = ImportType.DllImport,
                StrictSafety = true,
                StrictSafetySeverity = StrictSafetySeverity.Error,
                SingleFileOutputName = "Bindings.cs"
            };
            config.MarshallingMappings["bgcs_create_name"] = new FunctionMarshallingMapping
            {
                Return = new MarshallingMapping
                {
                    Strategy = MarshallingStrategy.String,
                    Ownership = BindingOwnership.Owned,
                    Encoding = BindingStringEncoding.Utf8,
                    RequiresCleanup = true,
                    CleanupFunction = "bgcs_free_name",
                    AllocatorKind = BindingAllocatorKind.NativeFunction,
                    AllocatorFunction = "bgcs_create_name",
                    NullTerminated = true
                }
            };
            config.MarshallingMappings["bgcs_free_name"] = new FunctionMarshallingMapping
            {
                Parameters =
                {
                    ["value"] = new MarshallingMapping
                    {
                        Strategy = MarshallingStrategy.Pointer,
                        Ownership = BindingOwnership.Transferred
                    }
                }
            };

            CsCodeGenerator generator = new(config);
            Assert.True(generator.Generate(header, Path.Combine(temp, "out")),
                string.Join(Environment.NewLine, generator.LastResult?.Diagnostics.Select(diagnostic => diagnostic.Message) ?? []));
            BindingFunction create = Assert.Single(generator.LastResult!.Module!.Functions,
                function => function.NativeName == "bgcs_create_name");
            Assert.Equal(BindingAllocatorKind.NativeFunction, create.ReturnMarshalling.AllocatorKind);
            Assert.DoesNotContain(generator.LastResult.Diagnostics,
                diagnostic => diagnostic.Code == BindingDiagnosticCodes.Allocator);
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
                ApiName = "IrNestedApi",
                Namespace = "BGCS.Tests.Generated",
                LibName = "ir_nested",
                ParserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                ImportType = ImportType.DllImport,
                AutoSquashTypedef = false,
                CSharpEmissionBackend = CSharpEmissionBackend.IntermediateRepresentation,
                SingleFileOutputName = "GeneratedBindings.cs"
            };

            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output));
            BindingModule module = generator.LastResult!.Module!;
            BindingType context = Assert.Single(module.Types,
                type => type.NativeName == "bgcs_context");
            Assert.Equal(BindingTypeKind.Structure, context.Kind);
            BindingType parent = Assert.Single(module.Types,
                type => type.Kind == BindingTypeKind.Structure && type.NativeName == "BgcsParent");
            Assert.Equal(3, parent.NestedTypes.Count);
            Assert.All(parent.Fields, field => Assert.False(string.IsNullOrWhiteSpace(field.ManagedName)));
            string source = string.Join(Environment.NewLine,
                generator.LastResult.OutputFiles.Select(File.ReadAllText));
            Assert.Contains($"partial struct {context.ManagedName}", source);
            Assert.All(parent.NestedTypes, nested => Assert.Contains($"partial struct {nested.ManagedName}", source));
            Assert.All(parent.Fields, field => Assert.Contains($"public {field.Type.ManagedName} {field.ManagedName};", source));
            Assert.DoesNotContain($"using {context.ManagedName} = void", source);
            Assert.All(generator.LastResult.OutputFiles, file =>
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
                ApiName = "IrInlineApi",
                Namespace = "BGCS.Tests.Generated",
                LibName = "ir_inline",
                ParserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                ImportType = ImportType.DllImport,
                CSharpEmissionBackend = CSharpEmissionBackend.IntermediateRepresentation,
                SingleFileOutputName = "GeneratedBindings.cs"
            };

            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output));
            BindingGenerationResult result = Assert.IsType<BindingGenerationResult>(generator.LastResult);
            BindingFunction function = Assert.Single(result.Module!.Functions);
            Assert.Equal("bgcs_public", function.NativeName);
            Assert.Equal(BindingFunctionKind.Free, function.Kind);
            string source = File.ReadAllText(Assert.Single(result.OutputFiles));
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
                ApiName = "IrBackendApi",
                Namespace = "BGCS.Tests.Generated",
                LibName = "ir_backend",
                ParserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                ImportType = ImportType.DllImport,
                CSharpEmissionBackend = CSharpEmissionBackend.IntermediateRepresentation,
                MergeGeneratedFilesToSingleFile = true,
                SingleFileOutputName = "GeneratedBindings.cs"
            };

            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output));
            BindingGenerationResult result = Assert.IsType<BindingGenerationResult>(generator.LastResult);
            string generated = Assert.Single(result.OutputFiles);
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
                ApiName = "IrBackendApi",
                Namespace = "BGCS.Tests.Generated",
                LibName = "ir_backend",
                ParserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                CSharpEmissionBackend = CSharpEmissionBackend.IntermediateRepresentation
            };

            CsCodeGenerator generator = new(config);

            Assert.False(generator.Generate(header, output));
            BindingGenerationResult result = Assert.IsType<BindingGenerationResult>(generator.LastResult);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == BindingDiagnosticCodes.CSharpUnsupported &&
                diagnostic.Message.Contains("variadic", StringComparison.Ordinal));
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
    [InlineData(BindingImportMode.FunctionTable, "funcTable.Load(0, \"bgcs_add\")")]
    public void CSharpEmitter_ImportModes_ShouldBeRepresentedByIr(BindingImportMode mode, string expected)
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-emitter-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            BindingModule module = new("NativeApi", "BGCS.Tests.Generated", "native", "host")
            {
                ImportMode = mode,
                UseCustomContext = mode == BindingImportMode.FunctionTable
            };
            BindingFunction function = new("bgcs_add", "BgcsAdd", BindingFunctionKind.Free,
                new("int", "int", 0, false, 4),
                new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed));
            function.Parameters.Add(new("left", "left", new("int", "int", 0, false, 4), BindingDirection.In,
                new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed)));
            if (mode == BindingImportMode.FunctionTable)
            {
                function.FunctionTableIndex = 0;
                module.FunctionTableEntries.Add(new(0, function.NativeName));
            }
            module.Functions.Add(function);

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
                ImportMode = mode,
                EmitLibraryNameConstant = false
            };
            module.Functions.Add(new BindingFunction("bgcs_add", "BgcsAdd", BindingFunctionKind.Free,
                new("int", "int", 0, false, 4),
                new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed)));

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
            type.Fields.Add(new BindingField("mode", "Mode", new("int", "int", 0, false, 4),
                0, 0, 3, [], true, true));
            type.Fields.Add(new BindingField("enabled", "Enabled", new("unsigned int", "uint", 0, false, 4),
                0, 3, 1, [], true));
            module.Types.Add(type);

            string emitted = Assert.Single(new CSharpEmitter().Emit(module, new(temp, true, "Bindings.cs")));
            string source = File.ReadAllText(emitted);
            Assert.Contains("LayoutKind.Explicit, Size = 4", source);
            Assert.Contains("Bitfield.GetSigned(RawBits0, 0, 3)", source);
            Assert.Contains("Bitfield.Get(RawBits1, 0, 1)", source);
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
    public void CSharpEmitter_EmptyUnion_ShouldEmitExplicitlyPositionedOpaqueStorage()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-emitter-empty-union-" + Guid.NewGuid().ToString("N"));
        try
        {
            BindingModule module = new("UnionApi", "BGCS.Tests.Generated", "union", "host");
            module.Types.Add(new BindingType("NativeUnion", "NativeUnion", BindingTypeKind.Union, 8, 8));

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
        type.Fields.Add(new BindingField("enabled", "Enabled", new("unsigned int", "uint", 0, false, 4),
            0, 0, 1, [], true));
        module.Types.Add(type);
        module.Functions.Add(new BindingFunction("log", "Log", BindingFunctionKind.Free,
            new("void", "void", 0, false, 0),
            new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed)) { IsVariadic = true });

        BindingEmissionException exception = Assert.Throws<BindingEmissionException>(() =>
            new CSharpEmitter().Emit(module, new(temp, true, "Bindings.cs")));

        Assert.Single(exception.Diagnostics);
        Assert.All(exception.Diagnostics, diagnostic => Assert.Equal(BindingDiagnosticCodes.CSharpUnsupported, diagnostic.Code));
        Assert.False(Directory.Exists(temp));
    }

    [Fact]
    public void CSharpEmitter_OpaqueStorageByValue_ShouldFailBeforeWritingOutput()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-emitter-opaque-value-" + Guid.NewGuid().ToString("N"));
        BindingModule module = new("UnsafeApi", "BGCS.Tests.Generated", "unsafe", "host");
        module.Types.Add(new BindingType("NativeHidden", "NativeHidden", BindingTypeKind.Structure, 16, 8)
        {
            IsOpaqueStorage = true
        });
        module.Functions.Add(new BindingFunction("consume", "Consume", BindingFunctionKind.Free,
            new("void", "void", 0, false, 0),
            new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed))
        {
            Parameters =
            {
                new BindingParameter("value", "value", new("NativeHidden", "NativeHidden", 0, false, 16),
                    BindingDirection.In, new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed))
            }
        });

        BindingEmissionException exception = Assert.Throws<BindingEmissionException>(() =>
            new CSharpEmitter().Emit(module, new(temp, true, "Bindings.cs")));

        BindingDiagnostic diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(BindingDiagnosticCodes.CSharpUnsupported, diagnostic.Code);
        Assert.Contains("opaque storage type 'NativeHidden' by value", diagnostic.Message);
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
            string source = File.ReadAllText(Assert.Single(generator.LastResult!.OutputFiles,
                file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains("Vector2 left", source, StringComparison.Ordinal);
            Assert.DoesNotContain("struct Vector2", source, StringComparison.Ordinal);
            Assert.Contains(generator.LastResult.Diagnostics, diagnostic =>
                diagnostic.Code == BindingDiagnosticCodes.ExternalType &&
                diagnostic.Severity == BindingDiagnosticSeverity.Warning);
            BindingExternalTypeContract contract = Assert.Single(generator.LastResult.Module!.ExternalTypes);
            Assert.Equal("Vector2", contract.ManagedType);
            Assert.True(contract.AllowsByValue);
            Assert.False(contract.LayoutValidationBypassed);
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
            rejected.ExternalTypeContracts[0].Size = 16;
            CsCodeGenerator rejectedGenerator = new(rejected);
            Assert.False(rejectedGenerator.Generate(header, Path.Combine(temp, "rejected")));
            Assert.Contains(rejectedGenerator.LastResult!.Diagnostics, diagnostic =>
                diagnostic.Code == BindingDiagnosticCodes.ExternalType &&
                diagnostic.Severity == BindingDiagnosticSeverity.Error);

            CsCodeGeneratorConfig bypassed = CreateExternalVectorConfig();
            bypassed.ExternalTypeContracts[0].Size = 16;
            bypassed.ExternalTypeContracts[0].ByValuePolicy = ExternalTypeByValuePolicy.BypassLayoutValidation;
            CsCodeGenerator bypassedGenerator = new(bypassed);
            Assert.True(bypassedGenerator.Generate(header, Path.Combine(temp, "bypassed")));
            Assert.Contains(bypassedGenerator.LastResult!.Diagnostics, diagnostic =>
                diagnostic.Code == BindingDiagnosticCodes.ExternalType &&
                diagnostic.Severity == BindingDiagnosticSeverity.Warning);
            Assert.True(Assert.Single(bypassedGenerator.LastResult.Module!.ExternalTypes).LayoutValidationBypassed);
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
                ApiName = "ExternalGenericApi",
                Namespace = "BGCS.Tests.Generated",
                LibName = "external_generic",
                ParserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                ImportType = ImportType.DllImport,
                SingleFileOutputName = "Bindings.cs"
            };
            config.TypeMappings["NativeVector_int"] = "NativeVector<int>";
            config.IgnoredTypes.Add("NativeVector_int");
            config.IgnoredTypedefs.Add("NativeVector_int");
            config.ExternalTypeContracts.Add(new()
            {
                NativeTypes = ["NativeVector_*"],
                ManagedTypes = ["NativeVector<*>"],
                Size = 16,
                Alignment = 8,
                ByValuePolicy = ExternalTypeByValuePolicy.RequireLayoutMatch
            });
            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output),
                string.Join(Environment.NewLine, generator.LastResult?.Diagnostics.Select(diagnostic => diagnostic.Message) ?? []));
            string source = File.ReadAllText(Assert.Single(generator.LastResult!.OutputFiles,
                file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains("NativeVector<int>", source, StringComparison.Ordinal);
            Assert.DoesNotContain("struct NativeVector<int>", source, StringComparison.Ordinal);
            BindingExternalTypeContract contract = Assert.Single(generator.LastResult.Module!.ExternalTypes);
            Assert.Equal("NativeVector<int>", contract.ManagedType);
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
                ApiName = "NativeApi",
                Namespace = "BGCS.Tests.Generated",
                LibName = "native",
                ParserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                ImportType = ImportType.DllImport,
                SingleFileOutputName = "Bindings.cs",
                GenerateDelegates = false,
                GenerateExtensions = false,
                WrapPointersAsHandle = true,
                MemberNamingConvention = NamingConvention.Unknown
            };
            config.FunctionMappings.Add(new("NativeThing_NativeThing", "NativeThing", null, [], []));
            config.FunctionMappings.Add(new("NativeThing_Reset", "Reset", null, [], []));
            config.FunctionMappings.Add(new("NativeThing_Resize", "Resize", null, [], []));
            config.FunctionMappings.Add(new("NativeThing_SetEnabled", "SetEnabled", null, [], []));
            config.FunctionMappings.Add(new("NativeBegin", "NativeBegin", null,
                new() { ["p_open"] = "NULL", ["flags"] = "0" }, []));
            config.FunctionMappings.Add(new("NativeFormat", "NativeFormat", null,
                new() { ["format"] = "\"%.3f\"" }, []));
            config.KnownMemberFunctions["NativeThing"] =
                ["NativeThing_NativeThing", "NativeThing_Reset", "NativeThing_Resize", "NativeThing_SetEnabled"];
            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output),
                string.Join(Environment.NewLine, generator.LastResult?.Diagnostics.Select(diagnostic => diagnostic.Message) ?? []));
            BindingFunction factory = Assert.Single(generator.LastResult!.Module!.Functions,
                function => function.NativeName == "NativeThing_NativeThing");
            BindingFunction reset = Assert.Single(generator.LastResult.Module.Functions,
                function => function.NativeName == "NativeThing_Reset");
            Assert.Equal(BindingManagedFunctionKind.Static, factory.ManagedKind);
            Assert.Equal(BindingManagedFunctionKind.Instance, reset.ManagedKind);
            Assert.Empty(generator.LastResult.Module.Delegates);
            string[] generatedSources = generator.LastResult.OutputFiles
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
                ApiName = "NativeApi",
                Namespace = "BGCS.Tests.Generated",
                LibName = "native",
                ParserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                ImportType = ImportType.DllImport,
                SingleFileOutputName = "Bindings.cs",
                GenerateExtensions = false,
                WrapPointersAsHandle = true
            };
            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output),
                string.Join(Environment.NewLine, generator.LastResult?.Diagnostics.Select(diagnostic => diagnostic.Message) ?? []));
            BindingType window = Assert.Single(generator.LastResult!.Module!.Types,
                type => type.NativeName.Contains("NativeWindow", StringComparison.Ordinal) &&
                    type.Kind == BindingTypeKind.OpaqueHandle);
            Assert.Equal("NativeWindow", window.ManagedName);
            BindingFunction create = Assert.Single(generator.LastResult.Module.Functions,
                function => function.NativeName == "Native_CreateWindow");
            Assert.Equal("NativeWindow", create.ReturnType.ManagedName);
            Assert.Equal(1, create.ReturnType.PointerDepth);
            BindingFunction getWindows = Assert.Single(generator.LastResult.Module.Functions,
                function => function.NativeName == "Native_GetWindows");
            Assert.Equal("NativeWindow*", getWindows.ReturnType.ManagedName);
            Assert.Equal(2, getWindows.ReturnType.PointerDepth);

            string[] generatedSources = generator.LastResult.OutputFiles
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
                Namespace = "Strict.Generated",
                ApiName = "StrictApi",
                LibName = "strict",
                ParserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                StrictSafetySeverity = StrictSafetySeverity.Error
            };
            CsCodeGenerator generator = new(config);

            Assert.False(generator.Generate(header, output));
            Assert.False(generator.LastResult!.Success);
            Assert.Contains(generator.LastResult.Diagnostics, diagnostic =>
                diagnostic.Code == "BGCS-SAFETY-OWNERSHIP" && diagnostic.Severity == BindingDiagnosticSeverity.Error);
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
            ApiName = "ExternalApi",
            Namespace = "BGCS.Tests.Generated",
            LibName = "external",
            ParserKind = BGCS.CppAst.Parsing.CppParserKind.C,
            ImportType = ImportType.DllImport,
            SingleFileOutputName = "Bindings.cs"
        };
        config.TypeMappings["NativeVector"] = "Vector2";
        config.IgnoredTypes.Add("NativeVector");
        config.IgnoredTypedefs.Add("NativeVector");
        config.Usings.Add("System.Numerics");
        config.ExternalTypeContracts.Add(new()
        {
            NativeTypes = ["NativeVector"],
            ManagedTypes = ["Vector2"],
            Size = 8,
            Alignment = 4,
            ByValuePolicy = ExternalTypeByValuePolicy.RequireLayoutMatch
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
            ApiName = "IrApi",
            Namespace = "BGCS.Tests.Generated",
            LibName = "ir",
            ImportType = ImportType.DllImport,
            GenerateExtensions = false,
            AutoSquashTypedef = false
        };
        config.MarshallingMappings["bgcs_get_items"] = new FunctionMarshallingMapping
        {
            Parameters =
            {
                ["output"] = new MarshallingMapping
                {
                    Strategy = MarshallingStrategy.Span,
                    Ownership = BindingOwnership.CallerAllocated,
                    LengthParameter = "actual_count",
                    CapacityParameter = "capacity",
                    WrittenCountParameter = "actual_count"
                }
            }
        };
        config.MarshallingMappings["bgcs_create_name"] = new FunctionMarshallingMapping
        {
            Return = new MarshallingMapping
            {
                Strategy = MarshallingStrategy.String,
                Ownership = BindingOwnership.Owned,
                Encoding = BindingStringEncoding.Utf8,
                CleanupFunction = "bgcs_free_name",
                RequiresCleanup = true,
                NullTerminated = true
            }
        };

        try
        {
            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output));

            BindingGenerationResult result = Assert.IsType<BindingGenerationResult>(generator.LastResult);
            Assert.True(result.Success);
            BindingConstant limit = Assert.Single(result.Module!.Constants, constant => constant.NativeName == "BGCS_LIMIT");
            Assert.Equal("int", limit.ManagedType);
            Assert.Equal("42", limit.Value);
            Assert.Single(result.Module.Constants, constant => constant.NativeName == "BGCS_LABEL");
            BindingType alias = Assert.Single(result.Module.Types, type => type.NativeName.Contains("BgcsId", StringComparison.Ordinal));
            Assert.Equal(BindingTypeKind.Alias, alias.Kind);
            Assert.Equal("uint", alias.UnderlyingType!.ManagedName);
            BindingDelegate callback = Assert.Single(result.Module.Delegates, value => value.NativeName == "BgcsCallback");
            Assert.Equal("void", callback.ReturnType.ManagedName);
            Assert.Single(callback.Parameters);
            BindingType item = Assert.Single(result.Module.Types, type => type.Kind == BindingTypeKind.Structure &&
                type.NativeName.Contains("BgcsItem", StringComparison.Ordinal));
            Assert.Equal(BindingTypeKind.Structure, item.Kind);
            Assert.Equal(20, item.Size);
            BindingField values = Assert.Single(item.Fields, field => field.NativeName == "values");
            Assert.Equal(new[] { 4 }, values.ArrayDimensions);
            BindingFunction function = Assert.Single(result.Module.Functions, value => value.NativeName == "bgcs_get_items");
            BindingParameter outputParameter = function.Parameters[0];
            Assert.Equal(MarshallingStrategy.Span, outputParameter.Marshalling.Strategy);
            Assert.Equal(BindingOwnership.CallerAllocated, outputParameter.Marshalling.Ownership);
            Assert.Equal("actual_count", outputParameter.Marshalling.LengthParameter);
            Assert.Equal("capacity", outputParameter.Marshalling.CapacityParameter);
            Assert.Equal("actual_count", outputParameter.Marshalling.WrittenCountParameter);
            BindingFunction createName = Assert.Single(result.Module.Functions, value => value.NativeName == "bgcs_create_name");
            Assert.Equal(BindingOwnership.Owned, createName.ReturnMarshalling.Ownership);
            Assert.Equal(BindingStringEncoding.Utf8, createName.ReturnMarshalling.StringEncoding);
            Assert.Equal("bgcs_free_name", createName.ReturnMarshalling.CleanupFunction);
            Assert.True(createName.ReturnMarshalling.RequiresCleanup);
            Assert.True(createName.ReturnMarshalling.NullTerminated);
            BindingDiagnostic safety = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "BGCS-SAFETY-OWNERSHIP");
            Assert.Contains("MarshallingMappings.bgcs_get_context.Return.Ownership", safety.Message);
            Assert.NotEmpty(result.OutputFiles);

            string irOutput = Path.Combine(temp, "ir-output");
            string emittedFile = Assert.Single(new CSharpEmitter().Emit(result.Module, new(irOutput, true, "Bindings.cs")));
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
