using BGCS.Configuration;
using Xunit;

namespace BGCS.Tests;

public sealed class CallbackDeclarationConfigurationTests
{
    [Fact]
    public void ExplicitDelegateOptOutSurvivesStrictConfigurationFreezing()
    {
        BindingConfiguration configuration = new()
        {
            schemaVersion = 1, library = new() { id = "callback-flags", nativeName = "callback-flags" },
            source = new() { headers = ["native.h"] }, api = new() { emitCallbackDelegates = false }
        };
        Assert.False(new BindingConfigurationLoader().ValidateAndFreeze(configuration).api.emitCallbackDelegates);
        Assert.True(new ApiConfiguration().emitCallbackDelegates);
    }
}
