using Xunit;

namespace BGCS.Configuration.Tests;

public class VaryingTypesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void VaryingTypes_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
