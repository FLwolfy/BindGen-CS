using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BGCS.Configuration;
using BGCS.Core.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class PresentationSelectionConfigurationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void GroupedReceiverWhitelistChangesOnlyManagedPresentationRequests(int count)
    {
        BindingConfiguration original = Configuration();
        ManagedReceiverRequest[] candidates = [Member("read"), Member("write")];
        BindingConfiguration frozen = BindingConfigurationLoader.ValidateAndFreeze(original with
        { api = original.api with { memberFunctions = [.. candidates.Take(count)] } });
        Assert.Equal(count, frozen.api.memberFunctions.Length);
        Assert.Equal(candidates.Take(count).Select(member => member.function), frozen.api.memberFunctions.Select(member => member.function));
        Assert.Equal(original.source.selection.includeDeclarations, frozen.source.selection.includeDeclarations);
        Assert.Equal(original.source.selection.excludeDeclarations, frozen.source.selection.excludeDeclarations);
        Assert.Empty(frozen.source.selection.includeDeclarations);
        Assert.Empty(frozen.source.selection.excludeDeclarations);
        Assert.Empty(frozen.api.pointerCarriers);
        Assert.Empty(frozen.api.pointerFunctions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupedAddressViewOptInKeepsSourceAndOwnershipInputsIndependent(bool enabled)
    {
        BindingConfiguration original = Configuration();
        BindingConfiguration frozen = BindingConfigurationLoader.ValidateAndFreeze(original with
        {
            api = original.api with
            {
                pointerCarriers = enabled ? [new("Node", "NodeAddress")] : [],
                pointerFunctions = enabled ? [new("read") { name = "ReadAddress", parameters = [new("self", "NodeAddress")] }] : []
            }
        });
        Assert.Equal(enabled ? 1 : 0, frozen.api.pointerCarriers.Length);
        Assert.Equal(enabled ? 1 : 0, frozen.api.pointerFunctions.Length);
        Assert.Empty(frozen.api.memberFunctions);
        Assert.Empty(frozen.source.selection.includeDeclarations);
        Assert.Empty(frozen.source.selection.excludeDeclarations);
        Assert.Equal(original.semantics.ownedResults, frozen.semantics.ownedResults);
    }

    [Fact]
    public void ExplicitEmptyChildSelectionsReplaceInheritedManagedViewsWithoutFilteringNativeSource()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-presentation-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "base.json"), """
                {"schemaVersion":2,"library":{"id":"selection","nativeName":"selection"},"source":{"headers":["native.h"]},
                 "api":{"memberFunctions":[{"function":"read","nativeType":"Node","receiver":"self","kind":"value"}],
                  "pointerCarriers":[{"nativeType":"Node","name":"NodeAddress"}],
                  "pointerFunctions":[{"function":"read","name":"ReadAddress","parameters":[{"parameter":"self","carrier":"NodeAddress"}]}]}}
                """);
            string child = Path.Combine(root, "bindgen.json");
            File.WriteAllText(child, """
                {"schemaVersion":2,"extends":["base.json"],"api":{"memberFunctions":[],"pointerCarriers":[],"pointerFunctions":[]}}
                """);
            BindingConfiguration frozen = new BindingConfigurationLoader(TestHostPaths.Create()).Load(child);
            Assert.Empty(frozen.api.memberFunctions);
            Assert.Empty(frozen.api.pointerCarriers);
            Assert.Empty(frozen.api.pointerFunctions);
            Assert.Equal(Path.Combine(root, "native.h"), Assert.Single(frozen.source.headers));
            Assert.Empty(frozen.source.selection.includeDeclarations);
            Assert.Empty(frozen.source.selection.excludeDeclarations);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("delegateMappings")]
    [InlineData("knownConstructors")]
    public void InertLegacyDictionariesDoNotBecomeUnverifiedNativeOverrideAliases(string field)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-retired-dictionary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "bindgen.json");
            File.WriteAllText(path, """
                {"schemaVersion":2,"library":{"id":"selection","nativeName":"selection"},"source":{"headers":["native.h"]},"__FIELD__":{}}
                """.Replace("__FIELD__", field, StringComparison.Ordinal));
            Assert.Throws<JsonException>(() => new BindingConfigurationLoader(TestHostPaths.Create()).Load(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ManagedReceiverRequest Member(string function) => new(function, "Node", "self") { kind = ManagedReceiverKind.Value };
    private static BindingConfiguration Configuration() => new()
    {
        schemaVersion = 2,
        library = new() { id = "selection", nativeName = "selection" },
        source = new() { headers = ["native.h"] }
    };
}
