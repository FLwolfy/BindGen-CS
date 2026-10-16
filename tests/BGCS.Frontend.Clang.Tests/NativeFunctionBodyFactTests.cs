using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Targets;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class NativeFunctionBodyFactTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefinitionFactsRemainExactWhenBodiesAreSkippedOrRetained(bool retainBodies)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-native-body-facts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "bodies.hpp");
            await File.WriteAllTextAsync(header, """
                int Declared(int value);
                inline int InlineDeclared(int value);
                static inline int Defined(int value) { return value + 1; }
                inline auto Deduced() { return 42; }
                inline int DeducedStorage = 42;
                inline decltype(auto) DeducedReference() { return (DeducedStorage); }
                auto Undeduced();
                struct BodyFacts
                {
                    BodyFacts() = default;
                    ~BodyFacts() = default;
                    void MethodDeclared();
                    void MethodDefined() {}
                };
                """);
            NativeTargetDescriptor target = new(new("body-facts"), "test", "test", "test", "x86_64-pc-windows-msvc", new());
            var module = (await new ClangFrontend().ParseAsync(new(target, SourceLanguage.Cpp, [header])
                { pathBase = root, retainFunctionBodies = retainBodies }, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.False(Assert.Single(module.declarations, declaration => declaration.name == "Declared").isDefinition);
            Assert.False(Assert.Single(module.declarations, declaration => declaration.name == "InlineDeclared").isDefinition);
            Assert.False(Assert.Single(module.declarations, declaration => declaration.name == "MethodDeclared").isDefinition);
            foreach (string name in new[] { "Defined", "Deduced", "MethodDefined", "BodyFacts", "~BodyFacts" })
                Assert.True(Assert.Single(module.declarations, declaration => declaration.kind == "Function" && declaration.name == name).isDefinition);
            NativeType deduced = Return("Deduced");
            Assert.Equal(NativeTypeCategory.Primitive, deduced.category);
            Assert.Equal("Int", deduced.primitiveKind);
            Assert.Equal(4, deduced.size);
            NativeType reference = Return("DeducedReference");
            Assert.Equal(NativeTypeCategory.Reference, reference.category);
            Assert.False(reference.isRValueReference);
            Assert.Equal(deduced.id, reference.elementType!.Value);
            NativeType undeduced = Return("Undeduced");
            Assert.Equal(NativeTypeCategory.Unexposed, undeduced.category);
            Assert.Null(undeduced.size);
            Assert.False(Assert.Single(module.declarations, declaration => declaration.name == "Undeduced").isDefinition);
            NativeType Return(string name) => module.GetType(Assert.Single(module.declarations,
                declaration => declaration.kind == "Function" && declaration.name == name).returnType!.Value);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
