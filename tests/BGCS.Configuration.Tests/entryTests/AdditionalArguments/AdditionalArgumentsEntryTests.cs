using Xunit;

namespace BGCS.Configuration.Tests;

public class AdditionalArgumentsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void AdditionalArguments_WithConfiguredArguments_ShouldGenerateAndMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void AdditionalArguments_WithoutConfiguredArguments_ShouldFailGeneration()
    {
        using var output = Generate("config.no-args.json", ["header.h"], ["header.h"]);
        AssertGenerationFailed(output);
    }
}
