using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Artifacts;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using BGCS.NativeBridge.Emission;
using BGCS.NativeBridge.Planning;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class NeutralSequenceAdapterTests
{
    [Theory]
    [InlineData("std::vector<int>")]
    [InlineData("std::array<int, 3>")]
    public void FiniteSequencePlan_PreservesPrimaryAndOriginalArgumentIdentity(string spelling)
    {
        NativeBridgePlan primary = Primary(spelling);
        BridgeExport original = primary.exports.Single(export => export.operation.declaration == new StableId("call"));
        NativeBridgePlan planned = new SequenceInputAdapterPlanner().Apply(primary, [new("Demo::Sum", [new("values")])]);
        Assert.Equal(original, planned.exports.Single(export => export.id == original.id));
        BridgeExport adapter = planned.exports.Single(export => export.inputAdapterOf == original.id);
        BridgeArgumentGroup group = Assert.Single(adapter.operation.argumentGroups);
        Assert.Equal(new StableId("parameter"), group.sourceParameter);
        Assert.Null(group.expression);
        Assert.Equal(EvidenceLevel.Declared, group.standardSequence!.evidence.level);
        Assert.Equal(StandardSequenceMaterialization.CopyValue, group.standardSequence.materialization);
        Sink sink = new();
        new TypedBridgeEmitter().Emit(planned, sink, "fixture");
        Assert.Contains("std::move(bgcs_sequence_value_0)", sink.files["src/fixture.cpp"]);
        Assert.Contains("BGCS nonempty sequence requires data", sink.files["src/fixture.cpp"]);
        if (spelling.StartsWith("std::array", StringComparison.Ordinal)) Assert.Contains("!= 3ULL", sink.files["src/fixture.cpp"]);
    }

    [Theory]
    [InlineData("unlinked")]
    [InlineData("self-link")]
    [InlineData("extent")]
    [InlineData("unknown-evidence")]
    [InlineData("element")]
    [InlineData("duplicate-input")]
    [InlineData("copy-to-borrow")]
    public void CorruptSequenceContracts_FailBeforeWritingArtifacts(string corruption)
    {
        NativeBridgePlan plan = new SequenceInputAdapterPlanner().Apply(Primary("std::array<int, 3>"), [new("Demo::Sum", [new("values")])]);
        BridgeExport adapter = plan.exports.Single(export => export.inputAdapterOf is not null);
        BridgeArgumentGroup group = Assert.Single(adapter.operation.argumentGroups);
        StandardSequenceArgument sequence = group.standardSequence!;
        BridgeExport changed = corruption switch
        {
            "unlinked" => adapter with { inputAdapterOf = null },
            "self-link" => adapter with { inputAdapterOf = adapter.id },
            "extent" => Change(sequence with { extent = 4 }),
            "unknown-evidence" => Change(sequence with { evidence = new(EvidenceLevel.Unknown, "missing") }),
            "element" => Change(sequence with { elementType = new("record") }),
            "duplicate-input" => adapter with { operation = adapter.operation with { argumentGroups = [group with { inputs = [group.inputs[0], group.inputs[0] with { name = "length" }] }] } },
            "copy-to-borrow" => Change(sequence with { materialization = StandardSequenceMaterialization.BorrowedView }),
            _ => throw new ArgumentException(nameof(corruption))
        };
        plan = plan with { exports = plan.exports.Select(export => export.id == changed.id ? changed : export).ToImmutableArray() };
        Sink sink = new();
        Assert.Throws<InvalidOperationException>(() => new TypedBridgeEmitter().Emit(plan, sink, "fixture"));
        Assert.Empty(sink.files);
        BridgeExport Change(StandardSequenceArgument value) => adapter with { operation = adapter.operation with { argumentGroups = [group with { standardSequence = value }] } };
    }

    private static NativeBridgePlan Primary(string spelling)
    {
        SourceLocation location = new("fixture.hpp", 1, 1);
        NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType record = new(new("record"), NativeTypeCategory.Record, spelling, 24, 8) { declaration = new("record-declaration") };
        NativeDeclaration declaration = new(new("record-declaration"), "Class", spelling, spelling, record.id, location)
        {
            isCompleteDefinition = true,
            isSystemDeclaration = true,
            templateArguments = spelling.StartsWith("std::array", StringComparison.Ordinal)
            ? [new("Type", "int", integer.id), new("AsInteger", "3", null)] : [new("Type", "int", integer.id)]
        };
        NativeDeclaration callable = new(new("call"), "Function", "Sum", "Demo::Sum", null, location) { returnType = integer.id, children = [new("parameter")] };
        NativeDeclaration parameter = new(new("parameter"), "Parameter", "values", "values", record.id, location) { parent = callable.id };
        NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [integer, record], [declaration, callable, parameter], []) { language = SourceLanguage.Cpp };
        NativeTargetDescriptor target = new(new(module.targetId), "windows", "x64", "msvc", module.triple, new());
        return new NativeBridgePlanner().Plan(new(module, target, "fixture", ["fixture.hpp"]));
    }

    private sealed class Sink : IArtifactSink
    {
        internal Dictionary<string, string> files { get; } = [];
        public string WriteText(
            string relativePath,
            string content
        ) { files.Add(relativePath, content); return relativePath; }
    }
}
