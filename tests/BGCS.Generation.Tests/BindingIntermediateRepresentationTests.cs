using System;
using System.IO;
using System.Linq;
using BGCS.Emission;
using BGCS.Intermediate;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public class BindingIntermediateRepresentationTests
{
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
            string source = File.ReadAllText(Assert.Single(generator.LastResult.OutputFiles));
            Assert.Contains($"partial struct {context.ManagedName}", source);
            Assert.All(parent.NestedTypes, nested => Assert.Contains($"partial struct {nested.ManagedName}", source));
            Assert.All(parent.Fields, field => Assert.Contains($"public {field.Type.ManagedName} {field.ManagedName};", source));
            Assert.DoesNotContain($"using {context.ManagedName} = void", source);
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
                SingleFileOutputName = "GeneratedBindings.cs"
            };

            CsCodeGenerator generator = new(config);

            Assert.True(generator.Generate(header, output));
            BindingGenerationResult result = Assert.IsType<BindingGenerationResult>(generator.LastResult);
            string generated = Assert.Single(result.OutputFiles);
            Assert.Equal("GeneratedBindings.cs", Path.GetFileName(generated));
            string source = File.ReadAllText(generated);
            Assert.Contains("public const int BGCS_LIMIT = 8;", source);
            Assert.Contains("public unsafe partial struct BgcsPoint", source);
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
            void* bgcs_get_context(void);
            """);
        CsCodeGeneratorConfig config = new()
        {
            ApiName = "IrApi",
            Namespace = "BGCS.Tests.Generated",
            LibName = "ir",
            ImportType = ImportType.DllImport,
            GenerateExtensions = false
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
            BindingType item = Assert.Single(result.Module.Types, type => type.NativeName.Contains("BgcsItem", StringComparison.Ordinal));
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
