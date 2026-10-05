using System;
using System.Collections.Generic;
using BGCS.Intermediate;
using Xunit;

namespace BGCS.Tests;

public sealed class BindingImmutabilityTests {
    [Fact]
    public void Module_FreezesContributionsAndCopiesReplacementCollections() {
        List<string> usings = ["Original.Namespace"];
        BindingModule module = new("Api", "Fixture", "fixture", "fixture-x64") { usings = usings };
        usings.Clear();

        List<string> replacement = ["Next.Namespace"];
        BindingModule next = module with { usings = replacement };
        replacement.Clear();

        Assert.Equal("Original.Namespace", Assert.Single(module.usings));
        Assert.Equal("Next.Namespace", Assert.Single(next.usings));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)module.usings).Add("Mutable.Namespace"));
    }

    [Fact]
    public void Type_FreezesNestedLayoutAndAttributes() {
        List<int> dimensions = [2, 3];
        BindingField field = new("values", "values", new("int[2][3]", "int", 0, false, 24), 0, 0, 0, dimensions);
        List<BindingField> fields = [field];
        List<string> attributes = ["StructLayout"];
        BindingType type = new("Values", "Values", BindingTypeKind.Structure, 24, 4) {
            fields = fields,
            attributes = attributes
        };
        dimensions.Clear();
        fields.Clear();
        attributes.Clear();

        Assert.Equal(new[] { 2, 3 }, Assert.Single(type.fields).arrayDimensions);
        Assert.Equal("StructLayout", Assert.Single(type.attributes));
        Assert.Throws<NotSupportedException>(() => ((IList<int>)field.arrayDimensions)[0] = 8);
    }

    [Fact]
    public void Function_FreezesParametersWithoutSharingMutableBuilderCollections() {
        List<BindingParameter> parameters = [new("value", "value", new("int", "int", 0, false, 4),
            BindingDirection.In, new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed))];
        BindingFunction function = new("consume", "Consume", BindingFunctionKind.Free,
            new("void", "void", 0, false, 0), new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed)) {
            parameters = parameters
        };
        parameters.Clear();

        Assert.Equal("value", Assert.Single(function.parameters).nativeName);
        Assert.Throws<NotSupportedException>(() => ((IList<BindingParameter>)function.parameters).Clear());
    }

    [Fact]
    public void Delegate_EnumAndExternalContract_FreezeTheirInputs() {
        List<BindingParameter> parameters = [];
        BindingDelegate callback = new("Callback", "Callback", new("void", "void", 0, false, 0),
            new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed)) { parameters = parameters };
        List<string> attributes = ["NativeName"];
        BindingEnumMember member = new("Original", "Original", "1") { attributes = attributes };
        List<string> aliases = ["native_carrier"];
        BindingExternalTypeContract contract = new(aliases, "Carrier", 16, 8, true, false);
        attributes.Clear();
        aliases.Clear();

        Assert.Empty(callback.parameters);
        Assert.Equal("NativeName", Assert.Single(member.attributes));
        Assert.Equal("native_carrier", Assert.Single(contract.nativeTypes));
    }
}
