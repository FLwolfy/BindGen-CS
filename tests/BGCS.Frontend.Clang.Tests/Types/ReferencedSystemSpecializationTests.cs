using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Frontend.Clang;
using BGCS.Platforms.Targeting;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class ReferencedSystemSpecializationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstantiatedSdkAliasesAndNestedRecords_KeepTheirExactConcreteOwner(bool reverseOrder)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-sdk-specializations-" + Guid.NewGuid().ToString("N"));
        string sdk = Path.Combine(root, "sdk");
        Directory.CreateDirectory(sdk);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(sdk, "storage.hpp"), """
                namespace SDK {
                template<class T> struct Types { using value_type = T; using pointer = value_type*; };
                template<class T> struct Forward;
                template<class Traits> struct Storage {
                    using pointer = typename Traits::pointer;
                    using value_type = typename Traits::value_type;
                    pointer first;
                    union Buffer { value_type inline_values[4]; pointer heap; } bytes;
                };
                int unrelated_sdk_api();
                }
                """);
            string header = Path.Combine(root, "entry.hpp");
            string chars = "SDK::Storage<SDK::Types<char>> chars();", integers = "SDK::Storage<SDK::Types<int>> integers();";
            string charLayout = "static_assert(sizeof(SDK::Storage<SDK::Types<char>>) > 0);\n";
            string integerLayout = "static_assert(sizeof(SDK::Storage<SDK::Types<int>>) > 0);\n";
            await File.WriteAllTextAsync(header, "#include <storage.hpp>\n" + (reverseOrder ? integerLayout + charLayout + integers + chars : charLayout + integerLayout + chars + integers)
                + """

                static_assert(sizeof(SDK::Storage<SDK::Types<char8_t>>) > 0);
                static_assert(sizeof(SDK::Storage<SDK::Types<char16_t>>) > 0);
                static_assert(sizeof(SDK::Storage<SDK::Types<wchar_t>>) > 0);
                SDK::Storage<SDK::Types<char8_t>> utf8units();
                SDK::Storage<SDK::Types<char16_t>> utf16units();
                SDK::Storage<SDK::Types<wchar_t>> wideunits();
                SDK::Forward<int> incomplete();
                """);
            NativeModule module = (await new ClangFrontend().ParseAsync(new(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp, [header])
            { systemIncludeDirectories = [sdk], pathBase = root, parseMacros = false, parseSystemIncludes = false }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration[] firstFields = new[] { "chars", "integers" }.Select(name =>
            {
                NativeDeclaration function = Assert.Single(module.declarations, declaration => declaration.kind == "Function" && declaration.name == name);
                NativeType result = Resolve(function.returnType!.Value);
                NativeDeclaration owner = module.GetDeclaration(result.declaration!.Value);
                Assert.Contains(name == "chars" ? "char" : "int", owner.qualifiedName);
                return Assert.Single(owner.children.Select(module.GetDeclaration), declaration => declaration.kind == "Field" && declaration.name == "first");
            }).ToArray();
            Assert.NotEqual(firstFields[0].id, firstFields[1].id);
            Assert.NotEqual(firstFields[0].type, firstFields[1].type);
            for (int index = 0; index < firstFields.Length; index++)
            {
                NativeType pointer = Resolve(firstFields[index].type!.Value);
                Assert.Equal(NativeTypeCategory.Pointer, pointer.category);
                Assert.Equal(index == 0 ? 1 : 4, Resolve(pointer.elementType!.Value).size);
                NativeDeclaration owner = module.GetDeclaration(firstFields[index].parent!.Value);
                NativeDeclaration bytes = Assert.Single(owner.children.Select(module.GetDeclaration), declaration => declaration.kind == "Field" && declaration.name == "bytes");
                NativeType buffer = Resolve(bytes.type!.Value);
                NativeDeclaration nested = module.GetDeclaration(buffer.declaration!.Value);
                Assert.Contains("Storage<", nested.qualifiedName);
                Assert.Contains(index == 0 ? "char" : "int", nested.qualifiedName);
                Assert.Equal(Math.Max(index == 0 ? 4 : 16, module.pointerSize), buffer.size);
                NativeDeclaration values = Assert.Single(nested.children.Select(module.GetDeclaration), declaration => declaration.kind == "Field" && declaration.name == "inline_values");
                NativeType array = Resolve(values.type!.Value);
                Assert.Equal(4, array.arrayLength);
                Assert.Equal(index == 0 ? 1 : 4, Resolve(array.elementType!.Value).size);
            }
            foreach ((string name, string primitive, long? width) in new[] { ("utf8units", "Char8", (long?)1), ("utf16units", "Char16", (long?)2), ("wideunits", "WChar", (long?)null) })
            {
                NativeDeclaration function = Assert.Single(module.declarations, declaration => declaration.kind == "Function" && declaration.name == name);
                NativeDeclaration owner = module.GetDeclaration(Resolve(function.returnType!.Value).declaration!.Value);
                NativeDeclaration field = Assert.Single(owner.children.Select(module.GetDeclaration), declaration => declaration.kind == "Field" && declaration.name == "first");
                NativeType pointer = Resolve(field.type!.Value);
                Assert.Equal(NativeTypeCategory.Pointer, pointer.category);
                NativeType element = Resolve(pointer.elementType!.Value);
                Assert.Equal(primitive, element.primitiveKind);
                if (width is not null) Assert.Equal(width, element.size);
                Assert.NotEqual(Resolve(firstFields[0].type!.Value).id, pointer.id);
                Assert.NotEqual(Resolve(firstFields[1].type!.Value).id, pointer.id);
            }
            Assert.DoesNotContain(module.declarations, declaration => declaration.name == "unrelated_sdk_api");
            NativeDeclaration incompleteFunction = Assert.Single(module.declarations, declaration => declaration.kind == "Function" && declaration.name == "incomplete");
            NativeType incompleteType = Resolve(incompleteFunction.returnType!.Value);
            Assert.Equal(NativeTypeCategory.Record, incompleteType.category);
            Assert.Null(incompleteType.size);
            NativeDeclaration incompleteRecord = module.GetDeclaration(incompleteType.declaration!.Value);
            Assert.False(incompleteRecord.isCompleteDefinition);
            Assert.Empty(incompleteRecord.children);

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
