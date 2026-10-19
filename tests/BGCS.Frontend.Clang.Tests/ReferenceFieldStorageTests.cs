using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class ReferenceFieldStorageTests
{
    [Theory]
    [InlineData("i686-pc-linux-gnu", 4)]
    [InlineData("x86_64-pc-linux-gnu", 8)]
    public async Task ObjectAndFunctionReferenceSlotsAreMeasuredSeparatelyFromTheirReferents(string triple, int slotSize)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-reference-slots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "references.hpp");
            await File.WriteAllTextAsync(header, """
                struct LargeReferent { char bytes[64]; };
                using ObjectReference = LargeReferent&;
                struct References { ObjectReference first; LargeReferent& second; int tail; };
                struct Variants { LargeReferent&& object; void (&function)(); void (&&rfunction)(); int tail; };
                References* borrow(References* value);
                Variants* borrow_variants(Variants* value);
                """);
            FrontendRequest request = new(new NativeTargetDescriptor(new("reference-fields"), "test", "test", "test", triple, new()), SourceLanguage.Cpp, [header])
                { pathBase = root, languageStandard = "c++23", parseMacros = false };
            NativeModule module = (await new ClangFrontend().ParseAsync(request, CancellationToken.None)).module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            NativeDeclaration references = Assert.Single(module.declarations, declaration => declaration.qualifiedName == "References");
            NativeDeclaration first = Field(references, "first"), second = Field(references, "second"), tail = Field(references, "tail");
            Assert.Equal(64, module.GetType(first.type!.Value).size);
            Assert.Equal(64, module.GetType(second.type!.Value).size);
            Assert.Equal(slotSize, first.storageSize);
            Assert.Equal(slotSize, second.storageSize);
            Assert.Equal(0, first.offset);
            Assert.Equal(slotSize, second.offset);
            Assert.Equal(2 * slotSize, tail.offset);
            Assert.Equal(4, tail.storageSize);
            NativeDeclaration variants = Assert.Single(module.declarations, declaration => declaration.qualifiedName == "Variants");
            foreach (string name in new[] { "object", "function", "rfunction" })
            {
                NativeDeclaration field = Field(variants, name);
                Assert.Equal(slotSize, field.storageSize);
                Assert.Equal(EvidenceLevel.Declared, field.storageEvidence!.level);
                Assert.StartsWith("clang.field-storage:", field.storageEvidence.origin);
            }
            Assert.Equal(3 * slotSize, Field(variants, "tail").offset);

            NativeDeclaration Field(NativeDeclaration parent, string name)
                => Assert.Single(parent.children.Select(module.GetDeclaration), declaration => declaration.kind == "Field" && declaration.name == name);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
