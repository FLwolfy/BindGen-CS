using Xunit;

namespace BGCS.Configuration.Tests;

public class CustomEnumsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void CustomEnums_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void CustomEnums_PlainTextComments_ShouldBeNormalizedToXmlComments()
    {
        using var output = Generate("config.comment-plain.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertBindingsExpected(output, "expected.bindings.comment-plain.json");
    }

    [Fact]
    public void CustomEnums_ExistingCommentSyntax_ShouldBeKeptAsCommentSyntax()
    {
        using var output = Generate("config.comment-preformatted.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertBindingsExpected(output, "expected.bindings.comment-preformatted.json");
    }
}
