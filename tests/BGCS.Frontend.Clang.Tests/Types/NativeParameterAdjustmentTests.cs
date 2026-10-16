using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class NativeParameterAdjustmentTests
{
    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task WrittenArraysAndFunctionParametersUseTheCompilersAdjustedPointerAbi(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-parameter-adjustment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "parameters.h");
            await File.WriteAllTextAsync(header, """
                typedef enum Backend { Backend_First, Backend_Second } Backend;
                typedef int Array[4];
                void fixed(int values[4]);
                void incomplete(char *values[]);
                void qualified(const Backend values[]);
                void matrix(int values[][3]);
                void alias(Array values);
                void callback(int handler(int value));
                typedef int (*ArrayCallback)(int values[3]);
                void callback_array(ArrayCallback callback);
                """ + (language == SourceLanguage.Cpp ? "void reference(int (&values)[4]);\n" : "void minimum(int values[static const 4]);\n"));
            NativeModule module = (await new ClangFrontend().ParseAsync(new(new TargetResolver().Resolve(new(new("host"))), language, [header])
            { pathBase = root, parseMacros = false }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            foreach (string name in new[] { "fixed", "incomplete", "qualified", "matrix", "alias", "callback" })
            {
                NativeDeclaration parameter = Parameter(name);
                NativeType abi = Resolve(parameter.type!.Value);
                Assert.Equal(NativeTypeCategory.Pointer, abi.category);
                Assert.Equal(module.pointerSize, abi.size);
                Assert.False(string.IsNullOrEmpty(parameter.sourceTypeSpelling));
            }
            Assert.Equal(NativeTypeCategory.Pointer, Resolve(Resolve(Parameter("incomplete").type!.Value).elementType!.Value).category);
            NativeType writtenFixed = Resolve(Parameter("fixed").sourceType!.Value);
            Assert.Equal(NativeTypeCategory.Array, writtenFixed.category);
            Assert.Equal(4, writtenFixed.arrayLength);
            NativeType writtenMatrix = Resolve(Parameter("matrix").sourceType!.Value);
            Assert.Equal(NativeTypeCategory.Array, writtenMatrix.category);
            Assert.Equal(3, Resolve(writtenMatrix.elementType!.Value).arrayLength);
            NativeType matrix = Resolve(Resolve(Parameter("matrix").type!.Value).elementType!.Value);
            Assert.Equal(NativeTypeCategory.Array, matrix.category);
            Assert.Equal(3, matrix.arrayLength);
            Assert.Equal(NativeTypeCategory.Function, Resolve(Resolve(Parameter("callback").type!.Value).elementType!.Value).category);
            NativeType callback = Resolve(Resolve(Parameter("callback_array").type!.Value).elementType!.Value);
            Assert.Equal(NativeTypeCategory.Function, callback.category);
            Assert.Equal(NativeTypeCategory.Pointer, Resolve(Assert.Single(callback.parameterTypes)).category);
            if (language == SourceLanguage.Cpp)
            {
                NativeType reference = Resolve(Parameter("reference").type!.Value);
                Assert.Equal(NativeTypeCategory.Reference, reference.category);
                Assert.Equal(NativeTypeCategory.Array, Resolve(reference.elementType!.Value).category);
            }
            else Assert.Equal(NativeTypeCategory.Pointer, Resolve(Parameter("minimum").type!.Value).category);
            NativeDeclaration Parameter(string function) => Assert.Single(Assert.Single(module.declarations, declaration => declaration.kind == "Function" && declaration.name == function)
                .children.Select(module.GetDeclaration), declaration => declaration.kind == "Parameter");
            NativeType Resolve(StableId id)
            {
                NativeType type = module.GetType(id);
                while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = module.GetType(type.elementType!.Value);
                return type;
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
