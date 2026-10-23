using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using BGCS.Targets.Desktop.Targeting;
using BGCS.Targets.Desktop.Toolchains;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class BorrowedTemplateArgumentTests : FrontendTestHost
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependentPairOverloadsKeepTheIteratorAliasAndTemplateParameterOwnedByTheirDeclaration(bool standardLibrary)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-borrowed-template-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "collection.hpp");
            await File.WriteAllTextAsync(header, (standardLibrary ? "#include <utility>\n"
                : "namespace std { template<class First, class Second> struct pair { First first; Second second; }; }\n") + """
                template<class T> struct Collection {
                    using iterator = T*;
                    iterator current;
                    std::pair<iterator, bool> insert();
                    std::pair<iterator, bool> insert(T);
                };
                """);
            NativeTargetDescriptor target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp);
            var parsed = await new ClangFrontend(host).ParseAsync(new(target, SourceLanguage.Cpp, [header])
            {
                pathBase = root, parseMacros = false,
                headerSearchPaths = ToolchainResolver.ResolveHeaderSearchPaths(target, SourceLanguage.Cpp).ToImmutableArray()
            }, CancellationToken.None);
            Assert.DoesNotContain(parsed.module.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.NotNull(parsed.parserBuiltinInputs);
            Assert.NotEmpty(parsed.parserBuiltinInputs.files);
            Assert.Equal(parsed.executionInputs, parsed.parserBuiltinInputs);
            NativeDeclaration collection = Assert.Single(parsed.module.declarations, declaration => declaration.name == "Collection");
            NativeDeclaration iterator = Assert.Single(parsed.module.declarations, declaration => declaration.name == "iterator" && declaration.parent == collection.id);
            NativeType alias = parsed.module.GetType(iterator.type!.Value);
            Assert.Equal(NativeTypeCategory.Alias, alias.category);
            NativeType pointer = parsed.module.GetType(alias.elementType!.Value);
            Assert.Equal(NativeTypeCategory.Pointer, pointer.category);
            NativeType parameter = parsed.module.GetType(Assert.Single(collection.templateParameters));
            Assert.Equal(NativeTypeCategory.Template, parameter.category);
            Assert.Equal("T", parameter.spelling);
            Assert.NotNull(pointer.elementType);
            Assert.NotNull(parsed.module.GetType(pointer.elementType.Value));
            NativeDeclaration[] overloads = parsed.module.declarations.Where(declaration => declaration.name == "insert" && declaration.parent == collection.id).ToArray();
            Assert.Equal(2, overloads.Length);
            Assert.Equal(new[] { 0, 1 }, overloads.Select(overload => overload.children.Length).Order().ToArray());
            Assert.All(overloads, overload => Assert.Contains("pair<", parsed.module.GetType(overload.returnType!.Value).spelling, StringComparison.Ordinal));
            Assert.Contains(parsed.module.declarations, declaration => declaration.name == "current" && declaration.parent == collection.id && declaration.type == iterator.type);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
