using System;
using System.Collections.Immutable;
using BGCS.Analysis.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class NeutralDurationPeriodTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompilerIntegralRatioFacts_AreNormalizedWithoutFloatingPointOrAnEpochClaim(bool timePoint)
    {
        NativeModule module = Module("AsInteger", "2", "4000");
        var period = Assert.IsType<BGCS.Core.Interop.NativeDurationPeriod>(new StandardLibraryFactReader().ReadPeriod(module, new(timePoint ? "point" : "duration")));
        Assert.Equal(1, period.numerator);
        Assert.Equal(2000, period.denominator);
        Assert.Equal(EvidenceLevel.Declared, period.evidence.level);
    }

    [Theory]
    [InlineData("Expression", "2", "4000")]
    [InlineData("AsInteger", "1 + 1", "4000")]
    [InlineData("AsInteger", "0", "4000")]
    [InlineData("AsInteger", "-2", "4000")]
    [InlineData("AsInteger", "2", "0")]
    [InlineData("AsInteger", "18446744073709551615", "1")]
    public void UnestablishedOrInvalidPeriod_RemainsUnknown(string kind, string numerator, string denominator)
        => Assert.Null(new StandardLibraryFactReader().ReadPeriod(Module(kind, numerator, denominator), new("duration")));

    private static NativeModule Module(string kind, string numerator, string denominator)
    {
        SourceLocation location = new("fixture.hpp", 1, 1);
        NativeType integer = new(new("integer"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeType ratio = new(new("period"), NativeTypeCategory.Record, "std::ratio<2,4000>", 1, 1) { declaration = new("ratio-declaration") };
        NativeType duration = new(new("duration"), NativeTypeCategory.Record, "std::chrono::duration<int,std::ratio<2,4000>>", 4, 4) { declaration = new("duration-declaration") };
        NativeType clock = new(new("clock"), NativeTypeCategory.Record, "Example::Clock", 1, 1) { declaration = new("clock-declaration") };
        NativeType point = new(new("point"), NativeTypeCategory.Record, "std::chrono::time_point<Example::Clock,std::chrono::duration<int,std::ratio<2,4000>>>", 4, 4) { declaration = new("point-declaration") };
        NativeDeclaration Record(string id, NativeType type, ImmutableArray<NativeTemplateArgument> arguments) => new(new(id), "Class", type.spelling, type.spelling, type.id, location)
        { templateArguments = arguments, isCompleteDefinition = true };
        return new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [integer, ratio, duration, clock, point],
            [Record("ratio-declaration", ratio, [new(kind, numerator, null), new(kind, denominator, null)]),
                Record("duration-declaration", duration, [new("Type", "int", integer.id), new("Type", ratio.spelling, ratio.id)]),
                Record("clock-declaration", clock, []), Record("point-declaration", point, [new("Type", clock.spelling, clock.id), new("Type", duration.spelling, duration.id)])], [])
        { language = SourceLanguage.Cpp, byteOrder = NativeByteOrder.LittleEndian };
    }
}
