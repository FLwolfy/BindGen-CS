using Xunit;

namespace BGCS.Configuration.Tests;

public class FunctionContainerMappingsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void FunctionContainerMapping_ShouldRouteFriendlyOverloadsAndKeepNativeStubsCentralized()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);

        AssertGenerationSucceeded(output);
        Assert.Contains("public unsafe partial class EntryApi", output.Bindings);
        Assert.Contains("internal static extern int AddNative(int left, int right);", output.Bindings);
        Assert.Contains("public unsafe partial class EntryInternals", output.Bindings);
        Assert.Contains("public static int Add(int left, int right)", output.Bindings);
        Assert.Contains("int ret = EntryApi.AddNative(left, right);", output.Bindings);
    }
}
