using System;
using System.Collections.Generic;
using BGCS.Core.Logging;
using Xunit;

namespace BGCS.Tests;

public class ComGuidExtractorTests
{
    [Fact]
    public void ExtractGuids_DefineGuidWith0x_ShouldPopulateGuidMap()
    {
        string input = """
                       #define DEFINE_GUID(name, l, w1, w2, b1,b2,b3,b4,b5,b6,b7,b8)
                       DEFINE_GUID(IID_IMyInterface, 0x12345678, 0x1234, 0x5678, 0x90, 0xab, 0xcd, 0xef, 0x12, 0x34, 0x56, 0x78);
                       """;

        CsCodeGeneratorConfig config = new();
        CsComCodeGenerator generator = new(config);
        ComGUIDExtractor extractor = new();
        List<(string, Guid)> guids = [];
        Dictionary<string, Guid> guidMap = [];

        extractor.ExtractGuids(input, config, generator, guids, guidMap);

        Assert.True(guidMap.TryGetValue("IMyInterface", out Guid guid));
        Assert.Equal(new Guid(0x12345678, 0x1234, 0x5678, 0x90, 0xab, 0xcd, 0xef, 0x12, 0x34, 0x56, 0x78), guid);
    }

    [Fact]
    public void ExtractGuids_SameNameDifferentValue_ShouldOverwriteAndWarn()
    {
        string inputA = """
                        #define DEFINE_GUID(name, l, w1, w2, b1,b2,b3,b4,b5,b6,b7,b8)
                        DEFINE_GUID(IID_IMyInterface, 0xaaaaaaaa, 0xbbbb, 0xcccc, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08);
                        """;
        string inputB = """
                        #define DEFINE_GUID(name, l, w1, w2, b1,b2,b3,b4,b5,b6,b7,b8)
                        DEFINE_GUID(IID_IMyInterface, 0x12345678, 0x1234, 0x5678, 0x90, 0xab, 0xcd, 0xef, 0x12, 0x34, 0x56, 0x78);
                        """;

        CsCodeGeneratorConfig config = new();
        CsComCodeGenerator generator = new(config);
        ComGUIDExtractor extractor = new();
        List<(string, Guid)> guids = [];
        Dictionary<string, Guid> guidMap = [];

        extractor.ExtractGuids(inputA, config, generator, guids, guidMap);
        extractor.ExtractGuids(inputB, config, generator, guids, guidMap);

        Assert.True(guidMap.TryGetValue("IMyInterface", out Guid guid));
        Assert.Equal(new Guid(0x12345678, 0x1234, 0x5678, 0x90, 0xab, 0xcd, 0xef, 0x12, 0x34, 0x56, 0x78), guid);
        Assert.Contains(generator.Messages, x => x.Severtiy == LogSeverity.Warning && x.Message.Contains("overwriting GUID", StringComparison.Ordinal));
    }
}
