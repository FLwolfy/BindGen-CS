using Xunit;

namespace BGCS.Configuration.Tests;

public class DelegatesAsVoidPointerEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void DelegatesAsVoidPointer_False_ShouldGenerateFunctionPointerSignatures()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void DelegatesAsVoidPointer_True_ShouldGenerateVoidPointerSignatures()
    {
        using var output = Generate("config.true.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.true.json", "expected.bindings.true.json");
    }
}
