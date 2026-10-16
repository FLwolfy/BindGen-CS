using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Targets;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class NeutralTemplateFactTests
{
    [Theory]
    [InlineData("i686-pc-windows-msvc", 4, 4)]
    [InlineData("aarch64-unknown-linux-gnu", 8, 8)]
    public async Task ParameterPackPreservesExactNeutralTypeArgumentsAndTargetWidths(string triple, int longSize, int pointerSize)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-template-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "types.hpp");
            await File.WriteAllTextAsync(header, "template<class... Types> class Values {}; Values<long, double, int*> create();");
            NativeTargetDescriptor target = new(new("template-pack-test"), "test", "test", "test", triple, new());
            NativeModule module = (await new ClangFrontend().ParseAsync(new(target, SourceLanguage.Cpp, [header])
                { pathBase = root, parseMacros = false }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration function = Assert.Single(module.declarations, declaration => declaration.kind == "Function" && declaration.name == "create");
            NativeType result = module.GetType(function.returnType!.Value);
            Assert.Equal(NativeTypeCategory.Record, result.category);
            NativeDeclaration specialization = module.GetDeclaration(result.declaration!.Value);
            NativeType[] arguments = specialization.templateArguments.Where(argument => argument.type is not null).Select(argument => module.GetType(argument.type!.Value)).ToArray();
            Assert.Equal(3, arguments.Length);
            Assert.Equal(new long?[] { longSize, 8, pointerSize }, arguments.Select(type => type.size));
            Assert.Equal("Long", arguments[0].primitiveKind);
            Assert.Equal("Double", arguments[1].primitiveKind);
            Assert.Equal(NativeTypeCategory.Pointer, arguments[2].category);
            Assert.Equal("Int", module.GetType(arguments[2].elementType!.Value).primitiveKind);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
