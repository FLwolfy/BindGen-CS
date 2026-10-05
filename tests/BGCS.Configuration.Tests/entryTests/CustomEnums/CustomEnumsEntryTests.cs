using BGCS.Metadata;
using Newtonsoft.Json;
using Xunit;

namespace BGCS.Configuration.Tests;

public class CustomEnumsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void CustomEnums_OmittedOptionalCollections_DeserializeAsEmpty()
    {
        const string json = """
            {"CppName":"NativeFlags","Name":"Flags","Items":[{"CppName":"NativeFlags_A","CppValue":"1"}]}
            """;

        CsEnumMetadata metadata = JsonConvert.DeserializeObject<CsEnumMetadata>(json)!;

        Assert.Equal("int", metadata.baseType);
        Assert.Empty(metadata.attributes);
        Assert.Single(metadata.items);
        Assert.Empty(metadata.items[0].attributes);
    }

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
