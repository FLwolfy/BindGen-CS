using System;
using System.IO;
using System.Linq;
using System.Collections.Immutable;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.NativeBuild.Planning;
using BGCS.NativeBuild.Validation;
using BGCS.Infrastructure.Execution;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeStorageAssertionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MsvcCEnumProofChecksSignednessAndPromotionRatherThanDistinctGenericIdentity(bool correct)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgcs enum native proof " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string compiler = MsvcToolchainDiscovery.FindCompiler("x64") ?? throw new FileNotFoundException("MSVC is required for the Windows enum proof gate.");
            MsvcToolchainInfo sdk = MsvcToolchainDiscovery.Resolve(compiler, "x64");
            string header = Path.Combine(root, "native.h"), source = Path.Combine(root, "proof.c");
            await File.WriteAllTextAsync(header, "typedef enum { Zero=0, One=1 } PublicEnum;\n");
            StableId integer = new("integer"), enumType = new("enum-type"), declarationId = new("enum");
            NativeDeclaration declaration = new(declarationId, "Enum", "PublicEnum", "PublicEnum", enumType, new(header, 1, 1)) { isCompleteDefinition = true };
            NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8,
                [new(integer, NativeTypeCategory.Primitive, correct ? "int" : "unsigned int", 4, 4) { primitiveKind = correct ? "Int" : "UInt", isSigned = correct },
                 new(enumType, NativeTypeCategory.Enumeration, "PublicEnum", 4, 4) { declaration = declarationId, elementType = integer }], [declaration], [])
                { language = SourceLanguage.C };
            string assertions = new NativeStorageAssertionEmitter().Emit(module, [header]);
            Assert.Contains("sizeof(PublicEnum)", assertions);
            Assert.DoesNotContain("sizeof(enum PublicEnum)", assertions);
            await File.WriteAllTextAsync(source, assertions);
            ImmutableArray<string> arguments = ["/nologo", "/c", "/TC", "/std:c17", "/Zc:preprocessor", "/external:W0", source, "/Fo" + Path.Combine(root, "proof.obj"),
                .. sdk.includeDirectories.Select(directory => "/external:I" + directory)];
            var compiled = await ProcessExecutor.ExecuteAsync(compiler, arguments, root, TimeSpan.FromSeconds(30));
            Assert.False(compiled.timedOut);
            if (correct) Assert.True(compiled.exitCode == 0, compiled.standardOutput + compiled.standardError);
            else
            {
                Assert.NotEqual(0, compiled.exitCode);
                Assert.Contains("BGCS native enum integer signedness", compiled.standardOutput + compiled.standardError);
                Assert.Contains("BGCS native enum integer promotion", compiled.standardOutput + compiled.standardError);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void NestedRecordInAnUnboundTemplateHasNoAddressableStorageAssertion()
    {
        NativeModule module = Module(specialized: false, system: false);
        Assert.False(NativeSourceFactComparer.RequiresVerification(module));
        string source = new NativeStorageAssertionEmitter().Emit(module, []);
        Assert.DoesNotContain("sizeof(Demo::Outer::Nested)", source);
        Assert.DoesNotContain("decltype(((Demo::Outer::Nested*)nullptr)->value)", source);
        Assert.Contains("sizeof(int)", source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcreteNestedStorageRetainsExactPublicFieldChecksIncludingSdkFacts(bool system)
    {
        NativeModule module = Module(specialized: true, system);
        if (!system) Assert.True(NativeSourceFactComparer.RequiresVerification(module));
        string source = new NativeStorageAssertionEmitter().Emit(module, []);
        Assert.Contains("sizeof(Demo::Outer<int>::Nested) == 4", source);
        Assert.Contains("alignof(Demo::Outer<int>::Nested) == 4", source);
        Assert.Contains("typedef int bgcs_verified_field_0;", source);
        Assert.Contains("decltype(((Demo::Outer<int>::Nested*)nullptr)->value)", source);
    }

    private static NativeModule Module(bool specialized, bool system)
    {
        StableId integer = new("int"), parameter = new("T"), owner = new("outer"), nested = new("nested"), field = new("field"), nestedType = new("nested-type");
        string ownerName = specialized ? "Demo::Outer<int>" : "Demo::Outer";
        NativeDeclaration container = new(owner, "Class", "Outer", ownerName, null, new("source.hpp", 1, 1))
        {
            role = "Struct", isCompleteDefinition = specialized, isSystemDeclaration = system, children = [nested],
            templateKind = specialized ? "ClassTemplateSpecialization" : "TemplateClass", templateParameters = specialized ? [] : [parameter]
        };
        NativeDeclaration record = new(nested, "Class", "Nested", ownerName + "::Nested", nestedType, new("source.hpp", 2, 1))
            { role = "Struct", parent = owner, children = [field], isCompleteDefinition = true, isSystemDeclaration = system };
        NativeDeclaration member = new(field, "Field", "value", ownerName + "::Nested::value", integer, new("source.hpp", 3, 1))
            { parent = nested, offset = 0, bitOffset = 0, visibility = "Public", isSystemDeclaration = system };
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8,
            [new(integer, NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true },
             new(parameter, NativeTypeCategory.Template, "T", null, null),
             new(nestedType, NativeTypeCategory.Record, record.qualifiedName, 4, 4) { declaration = nested }],
            [container, record, member], []) { language = SourceLanguage.Cpp };
    }
}
