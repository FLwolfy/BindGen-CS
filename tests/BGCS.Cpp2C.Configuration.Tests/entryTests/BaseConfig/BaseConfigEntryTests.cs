using Xunit;

namespace BGCS.Cpp2C.Configuration.Tests;

public class BaseConfigEntryTests : Cpp2CConfigurationEntryTestBase
{
    [Fact]
    public void BaseConfig_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate(
            "config.json",
            ["header.h"],
            ["header.h", "includes/dep.hpp"]);
        PrintGeneratedOutput(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
