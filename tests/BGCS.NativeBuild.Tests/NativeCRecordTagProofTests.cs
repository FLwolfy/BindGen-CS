using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.Execution;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class NativeCRecordTagProofTests
{
    [Theory]
    [InlineData("Struct", "struct")]
    [InlineData("Union", "union")]
    public async Task C17StorageAndCallableProofsKeepTagsDistinctFromSameNamedTypedefs(string role, string keyword)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-c-tag-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "native.h"), input = Path.Combine(root, "proof.c");
            await File.WriteAllTextAsync(header, keyword + " Pair { int value; };\ntypedef double Pair;\nint read_pair(" + keyword + " Pair* value);\n");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            target = ToolchainResolver.ResolveToolchain(target, SourceLanguage.C, "clang");
            StableId integer = new("int"), pair = new("pair"), pointer = new("pointer"), record = new("record"), field = new("field"), function = new("function"), parameter = new("parameter");
            NativeModule module = new(target.targetId.value, target.triple, 8,
                [new(integer, NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true },
                 new(pair, NativeTypeCategory.Record, "Pair", 4, 4) { declaration = record },
                 new(pointer, NativeTypeCategory.Pointer, "Pair *", 8, 8) { elementType = pair }],
                [new(record, "Class", "Pair", "Pair", pair, new(header, 1, 1)) { role = role, isCompleteDefinition = true, children = [field] },
                 new(field, "Field", "value", "Pair::value", integer, new(header, 1, 1)) { parent = record, visibility = "Public", offset = 0, bitOffset = 0 },
                 new(function, "Function", "read_pair", "read_pair", null, new(header, 3, 1)) { role = "Function", returnType = integer, children = [parameter], callingConvention = "C" },
                 new(parameter, "Parameter", "value", "value", pointer, new(header, 3, 1)) { parent = function }], []) { language = SourceLanguage.C };
            string proof = new NativeStorageAssertionEmitter().Emit(module, [header]);
            Assert.Contains("sizeof(" + keyword + " Pair) == 4", proof, StringComparison.Ordinal);
            Assert.Contains(keyword + " Pair *", proof, StringComparison.Ordinal);
            await File.WriteAllTextAsync(input, proof);
            ImmutableArray<string> arguments = ["--no-default-config", "--target=" + target.triple, "-std=c17", "-fsyntax-only", input,
                .. target.toolchain.systemIncludeFolders.SelectMany(directory => new[] { "-isystem", directory })];
            var compiled = await ProcessExecutor.ExecuteAsync(target.toolchain.compilerPath!, arguments, root, TimeSpan.FromSeconds(30));
            Assert.False(compiled.timedOut);
            Assert.True(compiled.exitCode == 0, compiled.standardOutput + compiled.standardError);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
