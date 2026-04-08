using Xunit;

namespace BGCS.Configuration.Tests;

public class GeneratePlaceholderCommentsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GeneratePlaceholderComments_False_ShouldNotEmitPlaceholder()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void GeneratePlaceholderComments_True_ShouldEmitPlaceholder()
    {
        using var output = Generate("config.true.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.true.json", "expected.bindings.true.json");
    }
}
