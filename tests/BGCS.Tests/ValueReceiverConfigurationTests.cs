using System;
using System.IO;
using BGCS.Configuration;
using BGCS.Contracts.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class ValueReceiverConfigurationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ValueMembersCannotDeclareAnObjectNameOrReleaseDomain(bool objectName)
    {
        BindingConfiguration configuration = new()
        {
            schemaVersion = 1, library = new() { id = "values", nativeName = "values" }, source = new() { headers = ["value.h"] },
            api = new() { memberFunctions = [new("increment", "NativeValue", "self")
                { kind = ManagedReceiverKind.Value, objectName = objectName ? "OwnedValue" : null, releaseDomain = objectName ? null : "free" }] }
        };
        Assert.Throws<ArgumentException>(() => new GenerationRequest(configuration, Path.GetTempPath()));
    }
}
