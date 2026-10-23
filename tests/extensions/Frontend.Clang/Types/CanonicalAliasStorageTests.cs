using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class CanonicalAliasStorageTests : FrontendTestHost
{
    [Theory]
    [InlineData(SourceLanguage.C, false)]
    [InlineData(SourceLanguage.Cpp, false)]
    [InlineData(SourceLanguage.C, true)]
    [InlineData(SourceLanguage.Cpp, true)]
    public async Task PlainSignedAndUnsignedCharKeepDistinctCanonicalTypesAndAliasPointerFacts(SourceLanguage language, bool unsignedPlainChar)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-char-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "characters.h");
            await File.WriteAllTextAsync(header, """
                typedef signed char SignedByte;
                typedef unsigned char UnsignedByte;
                struct Characters { char plain; signed char signedValue; unsigned char unsignedValue; SignedByte* signedPointer; UnsignedByte* unsignedPointer; };
                SignedByte EchoSigned(SignedByte value);
                void ReadSigned(SignedByte* output);
                char EchoPlain(char value);
                """);
            NativeTargetDescriptor target = new(new("char-identity-test"), "test", "test", "test", "x86_64-pc-windows-msvc", new());
            NativeModule module = (await new ClangFrontend(host).ParseAsync(new(target, language, [header])
            { pathBase = root, parseMacros = false, arguments = unsignedPlainChar ? ["-funsigned-char"] : [] }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration record = Assert.Single(module.declarations, declaration => declaration.kind == "Class" && declaration.name == "Characters");
            var fields = record.children.Select(module.GetDeclaration).Where(declaration => declaration.kind == "Field").ToDictionary(declaration => declaration.name);
            NativeType plain = Resolve(fields["plain"].type!.Value);
            NativeType signed = Resolve(fields["signedValue"].type!.Value);
            NativeType unsigned = Resolve(fields["unsignedValue"].type!.Value);
            Assert.Equal("char", plain.spelling);
            Assert.Equal("signed char", signed.spelling);
            Assert.Equal("unsigned char", unsigned.spelling);
            Assert.Equal(!unsignedPlainChar, plain.isSigned);
            Assert.True(signed.isSigned);
            Assert.False(unsigned.isSigned);
            Assert.Equal(3, new[] { plain.id, signed.id, unsigned.id }.Distinct().Count());
            Assert.Equal(signed.id, Resolve(Resolve(fields["signedPointer"].type!.Value).elementType!.Value).id);
            Assert.Equal(unsigned.id, Resolve(Resolve(fields["unsignedPointer"].type!.Value).elementType!.Value).id);
            NativeDeclaration echo = Assert.Single(module.declarations, declaration => declaration.kind == "Function" && declaration.name == "EchoSigned");
            Assert.Equal(signed.id, Resolve(echo.returnType!.Value).id);

            NativeType Resolve(BGCS.Core.Common.StableId id)
            {
                NativeType type = module.GetType(id);
                while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = module.GetType(type.elementType!.Value);
                return type;
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task NamedAndAnonymousEnumStorageRetainsTheCompilerUsableTypeSpelling(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-enum-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "enums.h");
            await File.WriteAllTextAsync(header, """
                typedef enum NegativeTag { Negative = -3, Positive = 19 } NegativeEnum;
                typedef enum { AnonymousNegative = -11, AnonymousPositive = 23 } PublicEnum;
                enum BareTag { UnreferencedValue = 37 };
                struct EnumFields { enum NegativeTag tagged; NegativeEnum named; PublicEnum anonymous; };
                NegativeEnum EchoNamed(NegativeEnum value);
                PublicEnum EchoAnonymous(PublicEnum value);
                """);
            NativeTargetDescriptor target = new(new("enum-storage-test"), "test", "test", "test", "x86_64-pc-windows-msvc", new());
            NativeModule module = (await new ClangFrontend(host).ParseAsync(new(target, language, [header]) { pathBase = root, parseMacros = false }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration named = Assert.Single(module.declarations, declaration => declaration.kind == "Enum" && declaration.name == "NegativeTag");
            NativeType namedStorage = module.GetType(named.type!.Value);
            Assert.Equal(language == SourceLanguage.C ? "enum NegativeTag" : "NegativeTag", namedStorage.spelling);
            NativeDeclaration bare = Assert.Single(module.declarations, declaration => declaration.kind == "Enum" && declaration.name == "BareTag");
            Assert.Equal(language == SourceLanguage.C ? "enum BareTag" : "BareTag", module.GetType(bare.type!.Value).spelling);
            NativeDeclaration anonymousAlias = Assert.Single(module.declarations, declaration => declaration.kind == "Typedef" && declaration.name == "PublicEnum");
            NativeType alias = module.GetType(anonymousAlias.type!.Value);
            NativeType anonymous = Resolve(alias.elementType!.Value);
            Assert.Equal(NativeTypeCategory.Enumeration, anonymous.category);
            Assert.Equal("PublicEnum", anonymous.spelling);
            Assert.Equal(namedStorage.size, anonymous.size);

            NativeType Resolve(BGCS.Core.Common.StableId id)
            {
                NativeType type = module.GetType(id);
                while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = module.GetType(type.elementType!.Value);
                return type;
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task QualifiedRecordUsesDoNotMutateTheSharedUnqualifiedDeclarationIdentity(SourceLanguage language)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-qualified-record-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "records.h");
            await File.WriteAllTextAsync(header, """
                struct Record { int value; };
                const struct Record* Read(void);
                volatile struct Record* Edit(void);
                const volatile struct Record* Inspect(void);
                """);
            NativeTargetDescriptor target = new(new("qualified-storage-test"), "test", "test", "test", "x86_64-pc-windows-msvc", new());
            NativeModule module = (await new ClangFrontend(host).ParseAsync(new(target, language, [header]) { pathBase = root, parseMacros = false }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration record = Assert.Single(module.declarations, declaration => declaration.kind == "Class" && declaration.name == "Record");
            Assert.Equal("Record", record.qualifiedName);
            NativeType storage = module.GetType(record.type!.Value);
            Assert.DoesNotContain("const ", storage.spelling);
            Assert.DoesNotContain("volatile ", storage.spelling);
            Assert.Contains(module.types, type => type.category == NativeTypeCategory.Qualified && type.qualifier == "Const");
            Assert.Contains(module.types, type => type.category == NativeTypeCategory.Qualified && type.qualifier == "Volatile");
            Assert.All(module.types.Where(type => type.category == NativeTypeCategory.Record), type => Assert.Equal(record.id, type.declaration));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("i686-pc-windows-msvc", 4)]
    [InlineData("x86_64-pc-windows-msvc", 8)]
    [InlineData("aarch64-unknown-linux-gnu", 8)]
    public async Task CanonicalizedAliasPointersRetainCompilerMeasuredStorage(string triple, int width)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-canonical-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "aliases.h");
            await File.WriteAllTextAsync(header, """
                typedef unsigned int Value;
                typedef Value* ValuePointer;
                typedef const Value* ConstValuePointer;
                typedef ValuePointer* NestedPointer;
                typedef ValuePointer PointerArray[3];
                ValuePointer ReturnPointer(void);
                ConstValuePointer ReturnConstPointer(void);
                NestedPointer ReturnNestedPointer(void);
                void ReadArray(PointerArray values);
                """);
            NativeTargetDescriptor target = new(new("canonical-storage-test"), "test", "test", "test", triple, new());
            NativeModule module = (await new ClangFrontend(host).ParseAsync(new(target, SourceLanguage.C, [header])
            { pathBase = root, parseMacros = false }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.Equal(width, module.pointerSize);
            Assert.NotEmpty(module.types.Where(type => type.category == NativeTypeCategory.Pointer));
            Assert.All(module.types.Where(type => type.category == NativeTypeCategory.Pointer), pointer =>
            {
                Assert.Equal(width, pointer.size);
                Assert.Equal(width, pointer.alignment);
            });
            foreach (NativeType alias in module.types.Where(type => type.category == NativeTypeCategory.Alias && type.canonicalType is not null))
            {
                NativeType canonical = module.GetType(alias.canonicalType!.Value);
                Assert.Equal(alias.size, canonical.size);
                Assert.Equal(alias.alignment, canonical.alignment);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
