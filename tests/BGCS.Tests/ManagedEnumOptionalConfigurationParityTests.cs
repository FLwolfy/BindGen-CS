using System;
using System.IO;
using System.Text.Json;
using BGCS.Configuration;
using BGCS.Core.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class ManagedEnumOptionalConfigurationParityTests
{
    [Fact]
    public void OmittedPresentationOptionsHaveTypedDefaultsAndNeverAdmitAttributeSourceStrings()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-enum-optional-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "bindgen.json");
            const string input = """
                {"schemaVersion":2,"library":{"id":"enum_defaults","nativeName":"enum_defaults"},
                 "source":{"headers":["native.h"]},
                 "api":{"enums":[{"name":"CustomMode","underlyingType":"int32","values":[{"name":"One","value":"1"}]}]}}
                """;
            File.WriteAllText(path, input);
            BindingConfiguration configuration = new BindingConfigurationLoader(TestHostPaths.Create()).Load(path);
            ManagedEnumDefinition declaration = Assert.Single(configuration.api.enums);
            Assert.Equal(ManagedIntegralKind.Int32, declaration.underlyingType);
            Assert.False(declaration.isFlags);
            Assert.Null(declaration.documentation);
            ManagedEnumValue member = Assert.Single(declaration.values);
            Assert.Equal("One", member.name);
            Assert.Equal("1", member.value);
            Assert.Null(member.documentation);
            Assert.Empty(configuration.api.nativeEnums);
            Assert.Empty(configuration.api.constants);
            File.WriteAllText(path, input.Replace("\"name\":\"CustomMode\"", "\"name\":\"CustomMode\",\"attributes\":[\"[System.Obsolete]\"]", StringComparison.Ordinal));
            Assert.Throws<JsonException>(() => new BindingConfigurationLoader(TestHostPaths.Create()).Load(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
