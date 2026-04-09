using Xunit;

namespace BGCS.Cpp2C.Configuration.Tests;

public class AdditionalArgumentsEntryTests : Cpp2CConfigurationEntryTestBase
{
    [Fact]
    public void AdditionalArguments_WithConfiguredArguments_ShouldGenerateAndMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintGeneratedOutput(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

}
