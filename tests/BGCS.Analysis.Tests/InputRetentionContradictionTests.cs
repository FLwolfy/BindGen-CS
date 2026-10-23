using BGCS.Analysis.Safety;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.Analysis.Tests;

public sealed class InputRetentionContradictionTests
{
    [Theory]
    [InlineData("value", true)]
    [InlineData("value", false)]
    [InlineData("value", null)]
    [InlineData("buffer", true)]
    [InlineData("buffer", false)]
    [InlineData("buffer", null)]
    [InlineData("text-parameter", true)]
    [InlineData("text-parameter", false)]
    [InlineData("text-parameter", null)]
    [InlineData("text-storage", true)]
    [InlineData("text-storage", false)]
    [InlineData("text-storage", null)]
    public void ExplicitRetainingOwnershipCannotBeOverriddenByANonretainingConvenienceContract(string protocol, bool? retainsPointer)
    {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:retention");
        LifetimeContract lifetime = new(NativeLifetimeKind.Call, null, evidence);
        OwnershipContract ownership = new(NativeOwnershipKind.Borrowed, lifetime, evidence) { retainsPointer = retainsPointer };
        InteropType unit = new(new("unit"), "unsigned char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "UnsignedChar", isSigned = false };
        InteropType pointer = new(new("pointer"), "unsigned char*", NativeTypeCategory.Pointer, 8, 8) { elementType = unit.id };
        InteropType count = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropParameter parameter = new(new("data"), "data", pointer.id);
        switch (protocol)
        {
            case "value":
                parameter = parameter with { ownership = ownership, singleValue = new(NativeParameterDirection.InputOutput, lifetime, evidence) { retainsPointer = false, allowsNull = false } };
                break;
            case "buffer":
                parameter = parameter with { ownership = ownership, buffer = new(unit.id, NativeLengthUnit.Elements, NativeParameterDirection.InputOutput, lifetime, evidence)
                { lengthParameter = new("length"), retainsPointer = false, allowsNull = false } };
                break;
            default:
                parameter = parameter with { ownership = protocol == "text-parameter" ? ownership : null,
                    text = new(NativeTextEncoding.Utf8, 1, NativeLengthUnit.CodeUnits, NativeParameterDirection.Input, lifetime, evidence)
                    { zeroTerminated = true, retainsPointer = false, allowsNull = false, ownership = protocol == "text-storage" ? ownership : null } };
                break;
        }
        InteropFunction function = new(new("read"), "read", count.id, [parameter, new(new("length"), "length", count.id)], "C");
        InteropModule module = new("retention", "windows-x64-msvc", 8, [unit, pointer, count], [function], []);
        string? restriction = protocol switch
        {
            "value" => new ValueParameterAnalyzer().GetRestriction(module, function, parameter),
            "buffer" => new FriendlyApiEligibilityAnalyzer().GetBufferInputRestriction(module, function, parameter),
            _ => new FriendlyApiEligibilityAnalyzer().GetTextInputRestriction(module, function, parameter)
        };
        Assert.Equal(retainsPointer == true, restriction is not null);
        Assert.Same(ownership, protocol == "text-storage" ? parameter.text!.ownership : parameter.ownership);
        Assert.False(protocol switch { "value" => parameter.singleValue!.retainsPointer, "buffer" => parameter.buffer!.retainsPointer, _ => parameter.text!.retainsPointer });
    }
}
