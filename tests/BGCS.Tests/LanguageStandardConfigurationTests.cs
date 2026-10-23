using System;
using System.IO;
using BGCS.Configuration;
using BGCS.Core.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class LanguageStandardConfigurationTests
{
    [Theory]
    [InlineData(SourceLanguage.C, "c++23")]
    [InlineData(SourceLanguage.C, "gnu++20")]
    [InlineData(SourceLanguage.Cpp, "c17")]
    [InlineData(SourceLanguage.Cpp, "gnu17")]
    [InlineData(SourceLanguage.Cpp, "iso9899:2017")]
    public void KnownMismatchedStandardFailsBeforeStartingAnyFrontend(SourceLanguage language, string standard)
    {
        ArgumentException rejected = Assert.Throws<ArgumentException>(() => new GenerationRequest(Configuration() with
        { source = new() { headers = ["native.h"], language = language, languageStandard = standard } }, Path.GetTempPath()));
        Assert.Contains("source.languageStandard", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SwitchingAFrozenRequestRequiresAnExplicitOrResetLanguageStandard()
    {
        GenerationRequest original = new(Configuration(), Path.GetTempPath());
        Assert.Equal("c17", original.configuration.source.languageStandard);
        Assert.Throws<ArgumentException>(() => original.WithConfiguration(original.configuration with
        { source = original.configuration.source with { language = SourceLanguage.Cpp } }));
        GenerationRequest cpp = original.WithConfiguration(original.configuration with
        { source = original.configuration.source with { language = SourceLanguage.Cpp, languageStandard = null } });
        Assert.Equal("c++23", cpp.configuration.source.languageStandard);
        Assert.Equal("c17", original.configuration.source.languageStandard);
    }

    private static BindingConfiguration Configuration() => new()
    {
        schemaVersion = 2, library = new() { id = "standards", nativeName = "standards" }, source = new() { headers = ["native.h"] }
    };
}
