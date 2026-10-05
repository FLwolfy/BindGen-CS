using System;
using System.IO;
using BGCS.Core.Configuration;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BGCS.Core.Tests;

public sealed class JsonConfigurationComposerTests
{
    [Fact]
    public void CompositionPreservesExplicitDefaultsAndRemovesIgnoredScalars()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-json-composition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "base.json"),
                """{"enabled":false,"name":"base","values":[1,2],"nested":{"remove":7,"keep":8}}""");
            JObject child = JObject.Parse(
                """{"baseConfig":{"url":"file://base.json","ignoredProperties":["name","nested.remove"]},"enabled":true,"values":[2,3]}""");
            JObject composed = JsonConfigurationComposer.Compose(child, directory);

            Assert.True(composed.Value<bool>("enabled"));
            Assert.Null(composed["name"]);
            Assert.Null(composed["nested"]!["remove"]);
            Assert.Equal(8, composed["nested"]!.Value<int>("keep"));
            Assert.Equal(new[] { 1, 2, 3 }, composed["values"]!.Values<int>());
            Assert.Null(composed["baseConfig"]);
            Assert.NotNull(child["baseConfig"]);
            Assert.Equal(new[] { 2, 3 }, child["values"]!.Values<int>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CircularAndMissingReferencesFailWithoutChangingTheChild()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-json-cycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "child.json");
            JObject child = JObject.Parse("""{"baseConfig":{"url":"file://child.json"},"name":"child"}""");
            File.WriteAllText(path, child.ToString());
            Assert.Throws<InvalidOperationException>(() => JsonConfigurationComposer.Compose(child, directory, path));
            child["baseConfig"]!["url"] = "file://missing.json";
            Assert.Throws<FileNotFoundException>(() => JsonConfigurationComposer.Compose(child, directory));
            Assert.Equal("child", child.Value<string>("name"));
            Assert.Equal("file://missing.json", child["baseConfig"]!.Value<string>("url"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
