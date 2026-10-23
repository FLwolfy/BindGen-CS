using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class DeclarationFactTests : FrontendTestHost
{
    [Theory]
    [InlineData("protected", "Protected")]
    [InlineData("private", "Private")]
    [InlineData("public", "Public")]
    public async Task InstantiatedNestedSdkTemplatePreservesItsDeclaredProtectedAccess(
        string access,
        string expected
    ) {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, $$"""
            namespace sdk_access {
            template<class T> struct Owner {
            {{access}}:
                template<class U> struct Hidden { U value; };
            public:
                Hidden<T> data;
            };
            }
            """);
        await File.WriteAllTextAsync(fixture.entry, """
            using SelectedOwner = sdk_access::Owner<int>;
            static_assert(sizeof(SelectedOwner) == sizeof(int));
            SelectedOwner transform(SelectedOwner value);
            """);
        FrontendResult result = await new ClangFrontend(host).ParseAsync(fixture.Request(), CancellationToken.None);
        Assert.DoesNotContain(result.module.diagnostics, diagnostic => diagnostic.isFailure);
        NativeDeclaration owner = Assert.Single(result.module.declarations,
            declaration => declaration.qualifiedName == "sdk_access::Owner<int>");
        NativeDeclaration data = Assert.Single(owner.children.Select(result.module.GetDeclaration), declaration => declaration.name == "data");
        NativeType hiddenType = result.module.GetType(data.type!.Value);
        NativeDeclaration hidden = result.module.GetDeclaration(hiddenType.declaration!.Value);
        Assert.True(hidden.isSystemDeclaration);
        Assert.True(hidden.isCompleteDefinition);
        Assert.Equal(expected, hidden.visibility);
        Assert.Equal(4, hiddenType.size);
        Assert.Contains(result.snapshot.files, input => input.path == fixture.header);
        Assert.True(SourceSnapshotVerifier.Matches(result.snapshot, host.paths));
    }

    [Fact]
    public async Task ExplicitTemplateSelfNamesResolveToTheirOwnersWithoutDuplicateNestedRecords()
    {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.entry, """
            namespace Demo {
            template<class T> class Selector {
            public: Selector() = default; Selector* Self() { return this; }
            };
            template<class T> class Selector<T*> {
            public: Selector() = default; Selector* Self() { return this; }
            };
            template<> class Selector<int> {
            public: Selector() = default; Selector* Self() { return this; }
            };
            }
            """);
        FrontendRequest request = fixture.Request() with
        {
            postamble = "template class Demo::Selector<int>;\ntemplate class Demo::Selector<float*>;\n"
        };
        NativeModule module = (await new ClangFrontend(host).ParseAsync(request, CancellationToken.None)).module;
        Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.DoesNotContain(module.declarations, declaration => declaration.kind == "Class" && declaration.qualifiedName == "Demo::Selector::Selector");
        foreach (string name in new[] { "Demo::Selector<int>", "Demo::Selector<float *>" })
        {
            NativeDeclaration owner = Assert.Single(module.declarations, declaration => declaration.kind == "Class" && declaration.qualifiedName == name);
            NativeType type = Assert.Single(module.types, type => type.category == NativeTypeCategory.Record && type.declaration == owner.id);
            Assert.Equal(owner.type, type.id);
            NativeDeclaration self = Assert.Single(owner.children.Select(module.GetDeclaration), declaration => declaration.name == "Self");
            NativeType pointer = module.GetType(self.returnType!.Value);
            Assert.Equal(NativeTypeCategory.Pointer, pointer.category);
            Assert.Equal(type.id, pointer.elementType);
            Assert.DoesNotContain(owner.children.Select(module.GetDeclaration), declaration => declaration.kind == "Class" && declaration.name == "Selector");
        }
    }

    [Fact]
    public async Task ReferencedSdkAliasChainsPreserveCompilerAccessBeforeLexicalMemberTraversal()
    {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, """
            namespace sdk_alias_access {
            template<class T> class Owner {
            private:
                typedef T HiddenTypedef;
                using HiddenUsing = T;
            protected:
                typedef T ProtectedTypedef;
                using ProtectedUsing = T;
            public:
                typedef HiddenTypedef PublicTypedef;
                using PublicUsing = HiddenUsing;
                typedef ProtectedTypedef PublicProtectedTypedef;
                using PublicProtectedUsing = ProtectedUsing;
            };
            }
            """);
        await File.WriteAllTextAsync(fixture.entry, """
            sdk_alias_access::Owner<int>::PublicTypedef typedef_value(sdk_alias_access::Owner<int>::PublicTypedef value);
            sdk_alias_access::Owner<int>::PublicUsing using_value(sdk_alias_access::Owner<int>::PublicUsing value);
            sdk_alias_access::Owner<int>::PublicProtectedTypedef protected_typedef_value(sdk_alias_access::Owner<int>::PublicProtectedTypedef value);
            sdk_alias_access::Owner<int>::PublicProtectedUsing protected_using_value(sdk_alias_access::Owner<int>::PublicProtectedUsing value);
            """);
        FrontendResult result = await new ClangFrontend(host).ParseAsync(fixture.Request(), CancellationToken.None);
        Assert.DoesNotContain(result.module.diagnostics, diagnostic => diagnostic.isFailure);
        foreach ((string name, string access) in new[]
        {
            ("HiddenTypedef", "Private"), ("HiddenUsing", "Private"),
            ("ProtectedTypedef", "Protected"), ("ProtectedUsing", "Protected"),
            ("PublicTypedef", "Public"), ("PublicUsing", "Public"),
            ("PublicProtectedTypedef", "Public"), ("PublicProtectedUsing", "Public")
        })
        {
            NativeDeclaration alias = Assert.Single(result.module.declarations, declaration => declaration.kind == "Typedef" && declaration.name == name);
            Assert.Equal(access, alias.visibility);
            Assert.True(alias.isSystemDeclaration);
            Assert.Equal("sdk_alias_access::Owner<int>", result.module.GetDeclaration(alias.parent!.Value).qualifiedName);
        }
        Assert.Contains(result.snapshot.files, input => input.path == fixture.header);
        Assert.True(SourceSnapshotVerifier.Matches(result.snapshot, host.paths));
    }

    [Fact]
    public async Task AnonymousAggregateFieldsPreserveCompilerAccessInPrimaryAndInstantiatedTemplates()
    {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.header, """
            namespace aggregate_access {
            template<class T> class Owner {
                union { T member; };
            public: int Read() const { return 0; }
            };
            template<class T> class PublicOwner {
            public: union { T member; };
            private: int Read() const { return 0; }
            };
            }
            """);
        string originalInclude = "#include \"" + fixture.header.Replace('\\', '/') + "\"\n";
        await File.WriteAllTextAsync(fixture.entry, originalInclude + """
            static_assert(sizeof(aggregate_access::Owner<int>) == sizeof(int));
            static_assert(sizeof(aggregate_access::PublicOwner<int>) == sizeof(int));
            void use(aggregate_access::Owner<int> hidden, aggregate_access::PublicOwner<int> visible);
            """);
        FrontendRequest request = fixture.Request() with { preamble = string.Empty, systemIncludeDirectories = [] };
        FrontendResult result = await new ClangFrontend(host).ParseAsync(request, CancellationToken.None);
        Assert.DoesNotContain(result.module.diagnostics, diagnostic => diagnostic.isFailure);
        foreach ((string name, string access) in new[] { ("Owner", "Private"), ("PublicOwner", "Public") })
        {
            Check(name + "<int>", access);
            Check(name + "<T>", access);
        }
        Assert.Contains(result.snapshot.files, input => input.path == fixture.header);
        Assert.True(SourceSnapshotVerifier.Matches(result.snapshot, host.paths));

        void Check(
            string name,
            string access
        ) {
            NativeDeclaration owner = Assert.Single(result.module.declarations,
                declaration => declaration.kind == "Class" && declaration.qualifiedName == "aggregate_access::" + name);
            NativeDeclaration field = Assert.Single(owner.children.Select(result.module.GetDeclaration), declaration => declaration.kind == "Field");
            Assert.Equal(access, field.visibility);
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly string root = Path.Combine(Path.GetTempPath(), "bgcs-declaration-facts-" + Guid.NewGuid().ToString("N"));
        internal readonly string header, entry;
        internal Fixture()
        {
            Directory.CreateDirectory(Path.Combine(root, "sdk"));
            header = Path.Combine(root, "sdk", "dependency.hpp"); entry = Path.Combine(root, "entry.hpp");
            File.WriteAllText(header, "struct Sdk {};\n"); File.WriteAllText(entry, "int original();\n");
        }
        internal FrontendRequest Request()
            => new(new NativeTargetDescriptor(new("declaration-facts"), "test", "test", "test", "x86_64-pc-linux-gnu", new()), SourceLanguage.Cpp, [entry])
            {
                pathBase = root,
                parseSystemIncludes = false,
                systemIncludeDirectories = [Path.GetDirectoryName(header)!],
                preamble = "#include <dependency.hpp>\n"
            };
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
