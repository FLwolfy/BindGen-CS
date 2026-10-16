using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Targets;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class NativeDeclarationStorageIdentityTests
{
    [Theory]
    [InlineData("x86_64-pc-windows-msvc")]
    [InlineData("x86_64-unknown-linux-gnu")]
    public async Task UnreferencedCRecordDeclarationsRetainCompilerTagAndAnonymousTypedefSpellings(string triple)
    {
        await WithModule(SourceLanguage.C, triple, """
            struct BareRecord { unsigned low : 1; unsigned high : 2; };
            union BareUnion { int integer; float real; };
            typedef struct TaggedRecord { int value; } RecordAlias;
            typedef struct { int value; } AnonymousRecord;
            typedef union { int integer; float real; } AnonymousUnion;
            """, module =>
        {
            Assert.DoesNotContain(module.declarations, declaration => declaration.kind == "Function");
            AssertStorage("BareRecord", "struct BareRecord");
            AssertStorage("BareUnion", "union BareUnion");
            AssertStorage("TaggedRecord", "struct TaggedRecord");
            AssertAlias("RecordAlias", "struct TaggedRecord");
            AssertAlias("AnonymousRecord", "AnonymousRecord");
            AssertAlias("AnonymousUnion", "AnonymousUnion");

            void AssertStorage(
                string name,
                string spelling
            ) {
                NativeDeclaration declaration = Assert.Single(module.declarations, declaration => declaration.kind == "Class" && declaration.name == name);
                NativeType storage = module.GetType(declaration.type!.Value);
                Assert.Equal(NativeTypeCategory.Record, storage.category);
                Assert.Equal(spelling, storage.spelling);
                Assert.Equal(4, storage.size);
                Assert.Equal(4, storage.alignment);
                Assert.Equal(declaration.id, storage.declaration);
            }
            void AssertAlias(
                string name,
                string spelling
            ) {
                NativeDeclaration declaration = Assert.Single(module.declarations, declaration => declaration.kind == "Typedef" && declaration.name == name);
                NativeType alias = module.GetType(declaration.type!.Value);
                Assert.Equal(NativeTypeCategory.Alias, alias.category);
                NativeType storage = Resolve(alias.elementType!.Value);
                Assert.Equal(NativeTypeCategory.Record, storage.category);
                Assert.Equal(spelling, storage.spelling);
                Assert.Equal(4, storage.size);
                Assert.Equal(4, storage.alignment);
            }
            NativeType Resolve(StableId id)
            {
                NativeType type = module.GetType(id);
                while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = module.GetType(type.elementType!.Value);
                return type;
            }
        });
    }

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task EnumItemIdentitiesUseScopedOwnersAndPreserveUnscopedInjection(SourceLanguage language)
    {
        string source = "enum Named { NamedValue = 21 };\nenum { AnonymousValue = 22 };\n";
        if (language == SourceLanguage.Cpp) source += """
            enum class Scoped : int { Value = 23 };
            namespace Demo {
                enum class Scoped : int { Value = 24 };
                enum Plain { PlainValue = 25 };
                enum { NamespaceValue = 26 };
                struct Holder {
                    enum class Nested : int { Value = 27 };
                    enum { MemberValue = 28 };
                };
            }
            """;
        await WithModule(language, "x86_64-unknown-linux-gnu", source, module =>
        {
            Item("NamedValue", "NamedValue", "21");
            Item("AnonymousValue", "AnonymousValue", "22");
            if (language == SourceLanguage.Cpp)
            {
                Item("Value", "Scoped::Value", "23");
                Item("Value", "Demo::Scoped::Value", "24");
                Item("PlainValue", "Demo::PlainValue", "25");
                Item("NamespaceValue", "Demo::NamespaceValue", "26");
                Item("Value", "Demo::Holder::Nested::Value", "27");
                Item("MemberValue", "Demo::Holder::MemberValue", "28");
                Assert.Equal(3, module.declarations.Count(declaration => declaration.kind == "EnumItem" && declaration.name == "Value"));
            }

            void Item(
                string name,
                string qualifiedName,
                string value
            ) {
                NativeDeclaration item = Assert.Single(module.declarations, declaration => declaration.kind == "EnumItem" && declaration.qualifiedName == qualifiedName);
                Assert.Equal(name, item.name);
                Assert.Equal(value, item.evaluatedIntegralValue);
                Assert.Equal("Enum", module.GetDeclaration(item.parent!.Value).kind);
            }
        });
    }

    private static async Task WithModule(
        SourceLanguage language,
        string triple,
        string source,
        Action<NativeModule> assertions
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-native-declaration-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, language == SourceLanguage.C ? "facts.h" : "facts.hpp");
            await File.WriteAllTextAsync(header, source);
            NativeTargetDescriptor target = new(new("native-declaration-identity"), "test", "test", "test", triple, new());
            NativeModule module = (await new ClangFrontend().ParseAsync(new(target, language, [header])
                { pathBase = root, parseMacros = false }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            assertions(module);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
