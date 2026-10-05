using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Cpp2C.Emission;
using BGCS.Cpp2C.Facade;
using BGCS.Intermediate.Bridges;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class CppBridgeIntermediateTests {
    [Fact]
    public void Generate_FrozenBridge_CanBeEmittedAfterDeletingSource() {
        string directory = CreateDirectory();
        try {
            string header = Path.Combine(directory, "counter.hpp");
            File.WriteAllText(header, "class Counter { public: int Add(int value) { return value + 1; } };");
            string original = Path.Combine(directory, "original");
            Cpp2CCodeGenerator generator = new(new());
            generator.Generate(header, original);
            Assert.True(generator.lastResult?.success,
                string.Join(Environment.NewLine, generator.lastResult?.diagnostics.Select(static value => value.message) ?? []));
            CppBridgeModule module = generator.lastResult!.module!;
            File.Delete(header);

            string repeated = Path.Combine(directory, "repeated");
            new CBridgeEmitter().Emit(module, new(repeated, false, string.Empty));

            Assert.Equal(File.ReadAllText(Path.Combine(original, "include", "Classes.h")),
                File.ReadAllText(Path.Combine(repeated, "include", "Classes.h")));
            Assert.Equal(File.ReadAllText(Path.Combine(original, "src", "Classes.cpp")),
                File.ReadAllText(Path.Combine(repeated, "src", "Classes.cpp")));
            Assert.Contains(module.artifacts, static artifact => artifact.operations.Any(
                static operation => operation.kind == CppBridgeOperationKind.BeginBlock));
        }
        finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Artifact_CopiesMutableAnalysisOperations() {
        List<CppBridgeOperation> operations = [new(CppBridgeOperationKind.Line, "int original;")];
        CppBridgeArtifact artifact = new("include/value.h", operations);
        operations.Clear();

        Assert.Equal("int original;", Assert.Single(artifact.operations).syntax);
        Assert.True(Assert.IsAssignableFrom<ICollection<CppBridgeOperation>>(artifact.operations).IsReadOnly);
    }

    [Theory]
    [InlineData("../outside.cpp")]
    [InlineData("include/../../outside.h")]
    public void Emit_EscapingArtifact_RejectsBeforeWritingAnyFiles(string invalidPath) {
        string directory = CreateDirectory();
        try {
            CppBridgeModule module = new("fixture", [], [], [
                new("include/valid.h", [new(CppBridgeOperationKind.Line, "int value;")]),
                new(invalidPath, [])
            ], []);

            Assert.Throws<InvalidOperationException>(() => new CBridgeEmitter().Emit(module, new(directory, false, string.Empty)));
            Assert.Empty(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories));
        }
        finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Emit_DuplicateOutput_RejectsBeforeWriting() {
        string directory = CreateDirectory();
        try {
            CppBridgeModule module = new("fixture", [], [], [new("same.h", []), new("same.h", [])], []);

            Assert.Throws<InvalidOperationException>(() => new CBridgeEmitter().Emit(module, new(directory, false, string.Empty)));
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(CppBridgeOperationKind.BeginBlock)]
    [InlineData(CppBridgeOperationKind.EndBlock)]
    [InlineData(CppBridgeOperationKind.Unindent)]
    public void Emit_UnbalancedOperation_RejectsBeforeWriting(CppBridgeOperationKind operation) {
        string directory = CreateDirectory();
        try {
            CppBridgeModule module = new("fixture", [], [], [new("invalid.cpp", [new(operation)])], []);

            Assert.Throws<InvalidOperationException>(() => new CBridgeEmitter().Emit(module, new(directory, false, string.Empty)));
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateDirectory() {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-bridge-ir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
