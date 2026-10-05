using System;
using System.Collections.Generic;
using BGCS.Core.Collections;
using BGCS.Metadata;
using Xunit;

namespace BGCS.Tests.Unit;

public sealed class MetadataOwnershipTests
{
    [Fact]
    public void DictionaryClonePreservesComparerAndSeparatesContainers()
    {
        Dictionary<string, int> source = new(StringComparer.OrdinalIgnoreCase) { ["Name"] = 7 };
        MetadataDictionaryEntry<string, int> entry = new(source);
        MetadataDictionaryEntry<string, int> clone = Assert.IsType<MetadataDictionaryEntry<string, int>>(entry.Clone());
        Assert.Equal(7, clone["NAME"]);
        clone["name"] = 9;
        Assert.Equal(7, entry["Name"]);
        Assert.Equal(7, source["Name"]);
    }

    [Fact]
    public void MergeCapturesPreviouslyAbsentEntriesAndHonorsOptionalFunctionTables()
    {
        CsCodeGeneratorMetadata source = new();
        source.entries.Add("custom", new MetadataListEntry<string>(["first"]));
        source.functionTable.entries.Add(new(0, "call"));
        CsCodeGeneratorMetadata destination = new();
        destination.Merge(source, default);
        Assert.False(destination.ContainsKey("FunctionTable"));
        MetadataListEntry<string> captured = destination.GetOrCreate<MetadataListEntry<string>>("custom");
        captured.Add("second");
        Assert.Single(source.GetOrCreate<MetadataListEntry<string>>("custom"));
        destination.Merge(source, new BGCS.Metadata.MergeOptions { mergeFunctionTable = true });
        Assert.Equal("call", Assert.Single(destination.functionTable.entries).entryPoint);
    }

    [Fact]
    public void TableMergeRejectsConflictsInsideTheIncomingBatchWithoutPartialMutation()
    {
        CsFunctionTableMetadata destination = new([new(0, "existing")]);
        CsFunctionTableMetadata conflicting = new([new(1, "first"), new(1, "second")]);
        Assert.Throws<InvalidOperationException>(() => destination.Merge(conflicting));
        Assert.Equal("existing", Assert.Single(destination.entries).entryPoint);
        CsFunctionTableMetadata source = new([new(1, "first"), new(1, "first")]);
        destination.Merge(source);
        Assert.Equal(2, destination.entries.Count);
        source.entries[0].entryPoint = "changed";
        Assert.Equal("first", destination.entries[1].entryPoint);
    }

    [Fact]
    public void CloneRetainsCustomConstantCarrierAndNullableTypedValues()
    {
        CsConstantMetadata constant = new("NAME", "7", CsConstantType.Int) { customType = "Carrier" };
        Assert.Equal("Carrier", constant.Clone().customType);
        MetadataListEntry<CloneableValue?> sequence = new([null, new(7)]);
        MetadataListEntry<CloneableValue?> clone = Assert.IsType<MetadataListEntry<CloneableValue?>>(sequence.Clone());
        Assert.Null(clone[0]);
        Assert.Equal(7, clone[1]!.value);
        Assert.NotSame(sequence[1], clone[1]);
    }

    public sealed class CloneableValue : ICloneable<CloneableValue>
    {
        public CloneableValue(int value) => this.value = value;
        public int value { get; }
        public CloneableValue Clone() => new(value);
    }
}
