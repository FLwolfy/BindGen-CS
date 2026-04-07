using Xunit;

namespace BGCS.Configuration.Tests;

public class CppLogLevelEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void CppLogLevel_Warning_ShouldIncludeCppWarningDiagnostics()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
        Assert.Contains("[Warning]", output.Diagnostics);
    }

    [Fact]
    public void CppLogLevel_Error_ShouldSuppressCppWarningDiagnostics()
    {
        using var output = Generate("config.error.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.error.json", "expected.bindings.json");
        Assert.DoesNotContain("[Warning]", output.Diagnostics);
    }
}
