using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Analysis.Interop;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class CompletedTypeIdentityTests : FrontendTestHost
{
    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public async Task RepeatedSharedTypesPreserveExactFactsRecursiveReferencesAndDeclarationOwnership(SourceLanguage language)
    {
        StringBuilder source = new("""
            typedef int Count;
            typedef Count CountAlias;
            typedef struct Node Node;
            struct Node { Node* next; Count value; };
            typedef Node* NodePointer;
            typedef Count (*CountCallback)(Count left, CountAlias right);

            """);
        for (int index = 0; index < 32; index++)
            source.Append("Count repeated_").Append(index).AppendLine("(Count left, CountAlias right, Node* node, NodePointer alias, CountCallback callback);");
        NativeModule first = (await ParseSourceAsync(source.ToString(), language: language)).module;
        NativeModule second = (await ParseSourceAsync(source.ToString(), language: language)).module;
        Assert.DoesNotContain(first.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.DoesNotContain(second.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.DoesNotContain(new NativeModuleVerifier().Validate(first), diagnostic => diagnostic.isFailure);
        Assert.DoesNotContain(new NativeModuleVerifier().Validate(second), diagnostic => diagnostic.isFailure);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));

        NativeDeclaration Alias(string name) => Assert.Single(first.declarations, declaration => declaration.kind == "Typedef" && declaration.name == name);
        StableId count = Alias("Count").type!.Value, countAlias = Alias("CountAlias").type!.Value;
        NativeType primitive = Assert.Single(first.types, type => type.category == NativeTypeCategory.Primitive && type.primitiveKind == "Int");
        Assert.Equal(4, primitive.size);
        Assert.Equal(4, primitive.alignment);
        Assert.True(primitive.isSigned);
        Assert.Equal(primitive.id, Unalias(count).id);
        Assert.Equal(primitive.id, Unalias(countAlias).id);
        NativeType node = Unalias(Alias("Node").type!.Value);
        Assert.Equal(NativeTypeCategory.Record, node.category);
        NativeDeclaration record = first.GetDeclaration(node.declaration!.Value);
        NativeDeclaration next = Assert.Single(record.children.Select(first.GetDeclaration), child => child.name == "next");
        NativeType recursive = Unalias(next.type!.Value);
        Assert.Equal(NativeTypeCategory.Pointer, recursive.category);
        Assert.Equal(node.id, Unalias(recursive.elementType!.Value).id);

        HashSet<StableId> ownedParameters = [];
        NativeDeclaration[] functions = first.declarations.Where(declaration => declaration.kind == "Function" && declaration.name.StartsWith("repeated_", StringComparison.Ordinal)).ToArray();
        Assert.Equal(32, functions.Length);
        foreach (NativeDeclaration function in functions)
        {
            Assert.Equal(count, function.returnType);
            NativeDeclaration[] parameters = function.children.Select(first.GetDeclaration).ToArray();
            Assert.Equal(5, parameters.Length);
            Assert.Equal(count, parameters[0].type);
            Assert.Equal(countAlias, parameters[1].type);
            Assert.Equal(node.id, Unalias(Unalias(parameters[2].type!.Value).elementType!.Value).id);
            Assert.Equal(node.id, Unalias(Unalias(parameters[3].type!.Value).elementType!.Value).id);
            NativeType callback = Unalias(Unalias(parameters[4].type!.Value).elementType!.Value);
            Assert.Equal(NativeTypeCategory.Function, callback.category);
            Assert.Equal(count, callback.returnType);
            Assert.Equal(new[] { count, countAlias }, callback.parameterTypes);
            foreach (NativeDeclaration parameter in parameters)
            {
                Assert.Equal(function.id, parameter.parent);
                Assert.True(ownedParameters.Add(parameter.id));
            }
        }
        Assert.Equal(160, ownedParameters.Count);

        NativeType Unalias(StableId identity)
        {
            HashSet<StableId> seen = [];
            NativeType type = first.GetType(identity);
            while (type.category == NativeTypeCategory.Alias)
            {
                Assert.True(seen.Add(type.id));
                type = first.GetType(type.elementType!.Value);
            }
            return type;
        }
    }
}
