using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using Xunit;

namespace BGCS.Tests;

public sealed class GroupedConfigurationCompositionTests
{
    [Fact]
    public void Composition_PreservesPrecedenceFalseListsAndEveryDeclaringPathBase()
    {
        using Scratch scratch = new();
        string parent = Path.Combine(scratch.root, "base");
        string child = Path.Combine(scratch.root, "child");
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(parent, "base.json"), """
            { "schemaVersion":1, "library":{"id":"example","nativeName":"example"},
              "source":{"headers":["native.h"],"includeDirectories":["include"],"defines":["BASE=1"]},
              "api":{"namespace":"Base.Namespace"}, "cache":{"enabled":true,"directory":"cache"},
              "profiles":{"dev":{"api":{"name":"ProfileApi","namespace":"Profile.Namespace"},
                "runtime":{"mode":"source"},"output":{"root":"profile-output"}}} }
            """);
        string entry = Path.Combine(child, "bindgen.json");
        File.WriteAllText(entry, """
            { "schemaVersion":1, "extends":["../base/base.json"],"profile":"dev",
              "source":{"defines":[]},"cache":{"enabled":false},"api":{"namespace":"Current.Namespace"} }
            """);
        GenerationRequest request = GenerationRequest.FromFile(entry);
        Assert.Equal("example", request.configuration.library.id);
        Assert.Equal("ProfileApi", request.configuration.api.name);
        Assert.Equal("Current.Namespace", request.configuration.api.@namespace);
        Assert.Equal("source", request.configuration.runtime.mode);
        Assert.False(request.configuration.cache.enabled);
        Assert.Empty(request.configuration.source.defines);
        Assert.Equal(Path.Combine(parent, "native.h"), Assert.Single(request.configuration.source.headers));
        Assert.Equal(Path.Combine(parent, "include"), Assert.Single(request.configuration.source.includeDirectories));
        Assert.Equal(Path.Combine(parent, "cache"), request.configuration.cache.directory);
        Assert.Equal(Path.Combine(parent, "profile-output"), request.configuration.output.root);
        Assert.Equal(2, request.configurationInputs.Length);
        Assert.Empty(request.configuration.extends);
        Assert.Empty(request.configuration.profiles);
        Assert.Equal(request.configurationInputs, request.WithConfiguration(request.configuration).configurationInputs);
    }

    [Fact]
    public void PluginSettings_PreserveTheirDeclaringDocumentBaseAndStrictWrapperFields()
    {
        using Scratch scratch = new();
        string parent = Path.Combine(scratch.root, "base");
        string child = Path.Combine(scratch.root, "child");
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(parent, "base.json"), """
            { "schemaVersion":1,"library":{"id":"example","nativeName":"example"},"source":{"headers":["native.h"]},
              "extensions":{"options":{"plugin.example":{"values":{"enabled":false,"count":0}}}} }
            """);
        string entry = Path.Combine(child, "bindgen.json");
        File.WriteAllText(entry, "{\"schemaVersion\":1,\"extends\":[\"../base/base.json\"]}");
        var settings = GenerationRequest.FromFile(entry).configuration.extensions.options["plugin.example"];
        Assert.Equal(parent, settings.pathBase);
        Assert.False(settings.values.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, settings.values.GetProperty("count").GetInt32());
        File.WriteAllText(entry, "{\"schemaVersion\":1,\"extends\":[\"../base/base.json\"],\"extensions\":{\"options\":{\"plugin.example\":{\"typo\":true}}}}");
        Assert.Throws<JsonException>(() => GenerationRequest.FromFile(entry));
    }

    [Fact]
    public void PartiallyMergedPluginSettings_PreserveEachValuesOriginalPathBase()
    {
        using Scratch scratch = new();
        string parent = Path.Combine(scratch.root, "base"), child = Path.Combine(scratch.root, "child");
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(parent, "base.json"), """
            { "schemaVersion":1,"library":{"id":"example","nativeName":"example"},"source":{"headers":["native.h"]},
              "extensions":{"options":{"plugin.example":{"values":{"inputs":{"header":"include/native.h","rules":"rules.json"},"files":["old.txt"],"empty":{},"a/b~c":"old.json"}}}} }
            """);
        string entry = Path.Combine(child, "bindgen.json");
        File.WriteAllText(entry, """
            { "schemaVersion":1,"extends":["../base/base.json"],"extensions":{"options":{"plugin.example":{
              "values":{"inputs":{"rules":"overrides.json"},"files":["new.txt"],"empty":false,"a/b~c":null}}}} }
            """);
        var settings = GenerationRequest.FromFile(entry).configuration.extensions.options["plugin.example"];
        var plugin = new BGCS.Contracts.Plugins.PluginConfiguration("plugin.example", settings.values, settings.pathBase!, settings.valuePathBases);
        Assert.Equal(Path.Combine(parent, "include", "native.h"), plugin.ResolveInputPath("/inputs/header"));
        Assert.Equal(Path.Combine(child, "overrides.json"), plugin.ResolveInputPath("/inputs/rules"));
        Assert.Equal(Path.Combine(child, "new.txt"), plugin.ResolveInputPath("/files/0"));
        Assert.Equal(child, plugin.GetPathBase("/empty"));
        Assert.Equal(child, plugin.GetPathBase("/a~1b~0c"));
        Assert.Throws<InvalidOperationException>(() => plugin.pathBase);
        Assert.Throws<InvalidOperationException>(() => plugin.GetPathBase("/inputs"));
        Assert.Throws<ArgumentException>(() => plugin.GetPathBase("/files/00"));
        Assert.Throws<ArgumentException>(() => plugin.GetPathBase("/a~2b"));
        Assert.Throws<ArgumentException>(() => plugin.GetPathBase("/missing"));
        File.WriteAllText(entry, "{\"schemaVersion\":1,\"extends\":[\"../base/base.json\"],\"extensions\":{\"options\":{\"plugin.example\":{\"valuePathBases\":{}}}}}");
        Assert.Throws<JsonException>(() => GenerationRequest.FromFile(entry));
    }

    [Fact]
    public void CyclesUnknownFieldsAndInvalidProfilesFailWithoutFallback()
    {
        using Scratch scratch = new();
        string first = Path.Combine(scratch.root, "first.json");
        string second = Path.Combine(scratch.root, "second.json");
        File.WriteAllText(first, "{\"schemaVersion\":1,\"extends\":[\"second.json\"]}");
        File.WriteAllText(second, "{\"schemaVersion\":1,\"extends\":[\"first.json\"]}");
        Assert.Contains("cycle", Assert.Throws<ArgumentException>(() => GenerationRequest.FromFile(first)).Message);
        File.WriteAllText(second, "{\"schemaVersion\":1,\"api\":{\"unknownProperty\":true}}");
        Assert.Throws<JsonException>(() => GenerationRequest.FromFile(first));
        File.WriteAllText(second, "{\"schemaVersion\":1,\"profiles\":{\"dev\":{\"profile\":\"other\"}}}");
        Assert.Throws<JsonException>(() => GenerationRequest.FromFile(first));
        File.WriteAllText(second, "{\"schemaVersion\":1,\"profiles\":{\"dev\":{\"api\":{\"unknownProperty\":true}}}}");
        Assert.Throws<JsonException>(() => GenerationRequest.FromFile(first));
        File.WriteAllText(second, "{\"schemaVersion\":1,\"profiles\":{\"unused\":{\"execution\":{\"sessionTimeoutSeconds\":0}}}}");
        Assert.Throws<ArgumentException>(() => GenerationRequest.FromFile(first));
    }

    [Fact]
    public async Task ChangedBaseInput_IsRejectedBeforeGenerationOrPublication()
    {
        using Scratch scratch = new();
        string parent = Path.Combine(scratch.root, "base.json");
        string entry = Path.Combine(scratch.root, "bindgen.json");
        File.WriteAllText(parent, "{\"schemaVersion\":1,\"library\":{\"id\":\"example\",\"nativeName\":\"example\"},\"source\":{\"headers\":[\"native.h\"]}}");
        File.WriteAllText(entry, "{\"schemaVersion\":1,\"extends\":[\"base.json\"]}");
        GenerationRequest request = GenerationRequest.FromFile(entry);
        File.AppendAllText(parent, "\n");
        bool created = false;
        await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithFrontendFactory(() => { created = true; throw new InvalidOperationException("Unexpected frontend creation."); }).Build();
        GenerationResult result = await host.GenerateAsync(request);
        Assert.False(result.success);
        Assert.False(created);
        Assert.Contains("configuration input changed", result.GetDiagnosticSummary());
        Assert.False(Directory.Exists(result.outputRoot));
    }

    private sealed class Scratch : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-grouped-config-" + Guid.NewGuid().ToString("N"));
        internal Scratch() => Directory.CreateDirectory(root);
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
