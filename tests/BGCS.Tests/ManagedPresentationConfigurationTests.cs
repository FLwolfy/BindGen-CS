using System;
using BGCS.Configuration;
using BGCS.Core.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class ManagedPresentationConfigurationTests
{
    [Theory]
    [InlineData("constant-array", "initialized")]
    [InlineData("enum-array", "initialized")]
    [InlineData("null-constant", "null")]
    [InlineData("blank-constant", "empty")]
    [InlineData("duplicate-constant", "multiple")]
    [InlineData("constant-value-expression", "either")]
    [InlineData("constant-kind", "undefined")]
    [InlineData("blank-carrier", "empty")]
    [InlineData("duplicate-enum", "multiple")]
    [InlineData("enum-member-array", "initialized")]
    [InlineData("duplicate-member", "multiple")]
    [InlineData("member-value-expression", "either")]
    public void InvalidPresentationInputsFailDuringConfigurationValidation(string invalid, string reason)
    {
        ApiConfiguration api = invalid switch
        {
            "constant-array" => new() { constants = default },
            "enum-array" => new() { nativeEnums = default },
            "null-constant" => new() { constants = [null!] },
            "blank-constant" => new() { constants = [new(" ")] },
            "duplicate-constant" => new() { constants = [new("LIMIT"), new("LIMIT")] },
            "constant-value-expression" => new() { constants = [new("LIMIT") { value = new(ManagedDefaultValueKind.Integer, "0"), expression = "1" }] },
            "constant-kind" => new() { constants = [new("LIMIT") { value = new((ManagedDefaultValueKind)999) }] },
            "blank-carrier" => new() { constants = [new("LIMIT") { managedType = " " }] },
            "duplicate-enum" => new() { nativeEnums = [new("Flags"), new("Flags")] },
            "enum-member-array" => new() { nativeEnums = [new("Flags") { values = default }] },
            "duplicate-member" => new() { nativeEnums = [new("Flags") { values = [new("None"), new("None")] }] },
            _ => new() { nativeEnums = [new("Flags") { values = [new("None") { value = new(ManagedDefaultValueKind.Integer, "0"), expression = "1" }] }] }
        };
        ArgumentException failure = Assert.ThrowsAny<ArgumentException>(() => BindingConfigurationLoader.ValidateAndFreeze(Configuration(api)));
        Assert.Contains(reason, failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitFalseZeroAndEmptyDocumentationSurviveFreezing()
    {
        ApiConfiguration api = new()
        {
            emitLibraryNameConstant = false,
            constants = [new("LIMIT") { value = new(ManagedDefaultValueKind.Integer, "0"), documentation = string.Empty }],
            nativeEnums = [new("Flags") { documentation = string.Empty, values = [new("None") { expression = "0" }] }]
        };
        ApiConfiguration frozen = BindingConfigurationLoader.ValidateAndFreeze(Configuration(api)).api;
        Assert.False(frozen.emitLibraryNameConstant);
        Assert.Equal("0", Assert.Single(frozen.constants).value!.value);
        Assert.Equal(string.Empty, Assert.Single(frozen.constants).documentation);
        ManagedNativeEnumRuleRequest enumeration = Assert.Single(frozen.nativeEnums);
        Assert.Equal(string.Empty, enumeration.documentation);
        Assert.Equal("0", Assert.Single(enumeration.values).expression);
    }

    private static BindingConfiguration Configuration(ApiConfiguration api) => new()
    {
        schemaVersion = 2,
        library = new() { id = "presentation", nativeName = "presentation" },
        source = new() { headers = ["native.h"] },
        api = api
    };
}
