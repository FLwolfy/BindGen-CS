using Xunit;

namespace BGCS.Configuration.Tests;

public class EnableExperimentalOptionsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void EnableExperimentalOptions_True_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void EnableExperimentalOptions_False_ShouldMatchExpected()
    {
        using var output = Generate("config.false.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.false.json", "expected.bindings.false.json");
    }
}
