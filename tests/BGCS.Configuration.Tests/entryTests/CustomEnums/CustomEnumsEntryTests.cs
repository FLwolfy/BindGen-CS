using Xunit;
using BGCS.Metadata;
using Newtonsoft.Json;

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

        Assert.Equal("int", metadata.BaseType);
        Assert.Empty(metadata.Attributes);
        Assert.Single(metadata.Items);
        Assert.Empty(metadata.Items[0].Attributes);
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
