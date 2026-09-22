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
    public void CSharpEmitter_UnsupportedSemantics_ShouldFailBeforeWritingOutput()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-emitter-validation-" + Guid.NewGuid().ToString("N"));
        BindingModule module = new("UnsafeApi", "BGCS.Tests.Generated", "unsafe", "host");
        BindingType type = new("Flags", "Flags", BindingTypeKind.Structure, 4, 4);
        type.Fields.Add(new BindingField("enabled", "Enabled", new("unsigned int", "uint", 0, false, 4),
            0, 0, 1, []));
        module.Types.Add(type);
        module.Functions.Add(new BindingFunction("log", "Log", BindingFunctionKind.Free,
            new("void", "void", 0, false, 0),
            new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed)) { IsVariadic = true });

        BindingEmissionException exception = Assert.Throws<BindingEmissionException>(() =>
            new CSharpEmitter().Emit(module, new(temp, true, "Bindings.cs")));

        Assert.Equal(2, exception.Diagnostics.Count);
        Assert.All(exception.Diagnostics, diagnostic => Assert.Equal(BindingDiagnosticCodes.CSharpUnsupported, diagnostic.Code));
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
            BindingType item = Assert.Single(result.Module!.Types, type => type.NativeName.Contains("BgcsItem", StringComparison.Ordinal));
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
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }
}
