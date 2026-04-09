using Xunit;

namespace BGCS.Cpp2C.Configuration.Tests;

public class DefinesEntryTests : Cpp2CConfigurationEntryTestBase
{
    [Fact]
    public void Defines_WithConfiguredValues_ShouldGenerateAndMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintGeneratedOutput(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

}
