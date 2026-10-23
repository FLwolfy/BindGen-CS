using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using BGCS.Infrastructure.Execution;
using BGCS.NativeBridge.Emission;
using BGCS.Targets.Desktop.Targeting;
using BGCS.Targets.Desktop.Toolchains;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCRecordTagProofTests
{
    [Theory]
    [InlineData("Struct", "struct", false)]
    [InlineData("Union", "union", false)]
    [InlineData("Struct", "struct", true)]
    [InlineData("Union", "union", true)]
    public async Task C17StorageAndCallableProofsKeepTagsDistinctFromSameNamedTypedefs(
        string role,
        string keyword,
        bool anonymous
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-c-tag-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "native.h"), input = Path.Combine(root, "proof.c");
            string storageType = anonymous ? "Pair" : keyword + " Pair";
            string declarationText = anonymous ? "typedef " + keyword + " { int value; } Pair;\n"
                : keyword + " Pair { int value; };\ntypedef double Pair;\n";
            await File.WriteAllTextAsync(header, declarationText + "int read_pair(" + storageType + "* value);\n");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            target = ToolchainResolver.ResolveToolchain(target, SourceLanguage.C);
            StableId integer = new("int"), pair = new("pair"), pointer = new("pointer"), record = new("record"), field = new("field"), function = new("function"), parameter = new("parameter");
            NativeModule module = new(target.targetId.value, target.triple, 8,
                [new(integer, NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true },
                 new(pair, NativeTypeCategory.Record, storageType, 4, 4) { declaration = record },
                 new(pointer, NativeTypeCategory.Pointer, storageType + " *", 8, 8) { elementType = pair }],
                [new(record, "Class", "Pair", "Pair", pair, new(header, 1, 1)) { role = role, isCompleteDefinition = true, children = [field] },
                 new(field, "Field", "value", "Pair::value", integer, new(header, 1, 1)) { parent = record, visibility = "Public", offset = 0, bitOffset = 0 },
                 new(function, "Function", "read_pair", "read_pair", null, new(header, 3, 1)) { role = "Function", returnType = integer, children = [parameter], callingConvention = "C" },
                 new(parameter, "Parameter", "value", "value", pointer, new(header, 3, 1)) { parent = function }], [])
            { language = SourceLanguage.C };
            string proof = new NativeStorageAssertionEmitter().Emit(module, [header], NativeAssertionConventions.For(module));
            Assert.Contains("sizeof(" + storageType + ") == 4", proof, StringComparison.Ordinal);
            Assert.Contains(storageType + " *", proof, StringComparison.Ordinal);
            if (anonymous) Assert.DoesNotContain(keyword + " Pair", proof, StringComparison.Ordinal);
            await File.WriteAllTextAsync(input, proof);
            ImmutableArray<string> arguments = ["-std=c17", "-fsyntax-only", input,
                .. target.toolchain.systemIncludeFolders.SelectMany(directory => new[] { "-isystem", directory })];
            var compiled = await ProcessExecutor.ExecuteAsync(NativeConsumerHarness.FindCompiler(false), arguments, root, TimeSpan.FromSeconds(30));
            Assert.False(compiled.timedOut);
            Assert.True(compiled.exitCode == 0, compiled.standardOutput + compiled.standardError);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
