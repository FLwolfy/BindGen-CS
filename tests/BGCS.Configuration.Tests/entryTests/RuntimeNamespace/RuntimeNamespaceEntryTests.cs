using Xunit;

namespace BGCS.Configuration.Tests;

public class RuntimeNamespaceEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void RuntimeNamespace_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
