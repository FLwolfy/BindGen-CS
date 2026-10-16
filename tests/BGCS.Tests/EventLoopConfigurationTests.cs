using System;
using BGCS.Configuration;
using BGCS.Contracts.Interop;
using Xunit;

namespace BGCS.Tests;

public sealed class EventLoopConfigurationTests
{
    [Fact]
    public void ExplicitNonblockingEventLoopFactsSurviveConfigurationFreezingWithoutBecomingDefaults()
    {
        BindingConfiguration configuration = Configuration();
        BindingConfiguration frozen = new BindingConfigurationLoader().ValidateAndFreeze(configuration);
        CallbackParameterConfiguration callback = Assert.Single(frozen.semantics.callbackParameters);
        Assert.Equal(NativeCallbackThreading.EventLoop, callback.threading);
        Assert.True(callback.registration!.unregisterIsNonBlocking);
        Assert.Equal("eventLoop", callback.registration.releaseThreadDomain);
        Assert.True(Assert.Single(frozen.semantics.asyncOperations).cancellationIsNonBlocking);
        Assert.Null(new CallbackRegistrationConfiguration().unregisterIsNonBlocking);
        Assert.Null(new AsyncOperationConfiguration().cancellationIsNonBlocking);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationAssertionRequiresAnExactNativeRequest(bool nonblocking)
    {
        BindingConfiguration configuration = Configuration();
        configuration = configuration with { semantics = configuration.semantics with
        {
            asyncOperations = [new() { function = "Start", completionCallback = "completion", resultArgumentIndex = 0,
                statusArgumentIndex = 1, cancellationIsNonBlocking = nonblocking }]
        } };
        Assert.Contains("cancelFunction", Assert.Throws<ArgumentException>(() => new BindingConfigurationLoader().ValidateAndFreeze(configuration)).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitBlockingAssertionRemainsFalseAndCannotBePromotedByFreezing()
    {
        BindingConfiguration configuration = Configuration();
        CallbackParameterConfiguration callback = configuration.semantics.callbackParameters[0];
        configuration = configuration with { semantics = configuration.semantics with
        {
            callbackParameters = [callback with { registration = callback.registration! with { unregisterIsNonBlocking = false } }],
            asyncOperations = [configuration.semantics.asyncOperations[0] with { cancellationIsNonBlocking = false }]
        } };
        BindingConfiguration frozen = new BindingConfigurationLoader().ValidateAndFreeze(configuration);
        Assert.False(Assert.Single(frozen.semantics.callbackParameters).registration!.unregisterIsNonBlocking);
        Assert.False(Assert.Single(frozen.semantics.asyncOperations).cancellationIsNonBlocking);
    }

    private static BindingConfiguration Configuration() => new()
    {
        schemaVersion = 1, library = new() { id = "event-loop", nativeName = "event-loop" }, source = new() { headers = ["native.h"] },
        semantics = new()
        {
            callbackParameters = [new()
            {
                function = "Start", parameter = "completion", lifetime = NativeLifetimeKind.Operation, retained = true,
                threading = NativeCallbackThreading.EventLoop, userDataParameter = "state", callableUserDataIndex = 2,
                unregisterFunction = "Stop", unregisterQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations,
                registration = new()
                {
                    unregisterHandleParameter = "handle", nullResultQuiescence = NativeQuiescenceKind.NoActiveNativeInvocations,
                    releaseThreadDomain = "eventLoop", unregisterIsNonBlocking = true
                }
            }],
            asyncOperations = [new()
            {
                function = "Start", completionCallback = "completion", completionQuiescence = NativeQuiescenceKind.NoNewInvocations,
                resultArgumentIndex = 0, statusArgumentIndex = 1, cancelFunction = "Cancel", cancelHandleParameter = "handle",
                cancellationIsNonBlocking = true
            }]
        }
    };
}
