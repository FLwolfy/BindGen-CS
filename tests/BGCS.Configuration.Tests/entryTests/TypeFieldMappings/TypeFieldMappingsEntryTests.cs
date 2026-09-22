using Xunit;

namespace BGCS.Configuration.Tests;

public class TypeFieldMappingsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void TypeFieldMappings_ShouldPreserveExplicitManagedNamesEverywhere()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);

        AssertGenerationSucceeded(output);
        Assert.Contains("public int ID;", output.Bindings);
        Assert.Contains("public byte URL_0;", output.Bindings);
        Assert.Contains("public Span<byte> URL", output.Bindings);
        Assert.Contains("public int CPU", output.Bindings);
        Assert.Contains("public unsafe SampleType(int id = default, byte* url = default, int cpu = default)", output.Bindings);
        Assert.Contains("ID = id;", output.Bindings);
        Assert.Contains("CPU = cpu;", output.Bindings);
    }
}
