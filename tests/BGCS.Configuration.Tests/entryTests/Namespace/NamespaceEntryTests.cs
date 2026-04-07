using Xunit;

namespace BGCS.Configuration.Tests;

public class NamespaceEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void Namespace_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
