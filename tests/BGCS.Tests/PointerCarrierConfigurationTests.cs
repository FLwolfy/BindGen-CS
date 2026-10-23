using System;
using System.IO;
using System.Threading.Tasks;
using BGCS.Configuration;
using Xunit;

namespace BGCS.Tests;

public sealed class PointerCarrierConfigurationTests
{
    [Fact]
    public async Task ExplicitContextReceiverSurvivesStrictGroupedConfigurationLoading()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-address-receiver-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "bindgen.json");
            await File.WriteAllTextAsync(path, """
                { "schemaVersion":2,"library":{"id":"address","nativeName":"address"},"source":{"headers":["native.h"]},
                  "api":{"pointerCarriers":[{"nativeType":"Node","name":"NodeAddress"}],
                    "pointerFunctions":[{"function":"read_node","name":"Read","receiver":"self","parameters":[{"parameter":"self","carrier":"NodeAddress"}]}]} }
                """);
            GenerationRequest request = await TestInputs.LoadRequestAsync(path);
            Assert.Equal("self", Assert.Single(request.configuration.api.pointerFunctions).receiver);
            await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("\"receiver\":\"self\"", "\"receiver\":\" \"", StringComparison.Ordinal));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => TestInputs.LoadRequestAsync(path));
            Assert.Equal("self", Assert.Single(request.configuration.api.pointerFunctions).receiver);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PointerCarriersRequireAnExistingPositiveIndirection(int depth)
    {
        BindingConfiguration configuration = new()
        {
            schemaVersion = 2, library = new() { id = "addresses", nativeName = "addresses" }, source = new() { headers = ["native.h"] },
            api = new() { pointerCarriers = [new("NativeValue", "ValueAddress") { pointerDepth = depth }] }
        };
        Assert.Throws<ArgumentException>(() => new GenerationRequest(configuration, Path.GetTempPath()));
    }

    [Fact]
    public void PointerFunctionsMustDeclareTheirFiniteAddressChannels()
    {
        BindingConfiguration configuration = new()
        {
            schemaVersion = 2, library = new() { id = "addresses", nativeName = "addresses" }, source = new() { headers = ["native.h"] },
            api = new() { pointerFunctions = [new("native_read")] }
        };
        Assert.Throws<ArgumentException>(() => new GenerationRequest(configuration, Path.GetTempPath()));
    }
}
