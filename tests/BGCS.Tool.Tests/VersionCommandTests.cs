using System;
using System.IO;
using System.Text.Json;
using BGCS.Contracts.Versioning;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class VersionCommandTests
{
    [Theory]
    [InlineData("version")]
    [InlineData("--version")]
    public void JsonVersion_ReportsCentralContractsModelsAndExplicitParserDeployment(string command)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        Assert.Equal(0, CliInvocation.Run([command, "--json"], Environment.CurrentDirectory, output, error));
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        JsonElement root = json.RootElement;
        Assert.Equal("BindGen-CS", root.GetProperty("product").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("runtimeVersion").GetString()));
        Assert.Equal(ContractVersion.C_MAJOR, root.GetProperty("contracts").GetProperty("major").GetInt32());
        Assert.Equal(ContractVersion.C_API_MINOR, root.GetProperty("contracts").GetProperty("apiMinor").GetInt32());
        Assert.Equal(ModelRevision.C_NATIVE, root.GetProperty("models").GetProperty("native").GetInt32());
        Assert.Equal(ModelRevision.C_CPP_BRIDGE, root.GetProperty("models").GetProperty("cppBridge").GetInt32());
        Assert.Equal(ModelRevision.C_FRONTEND_PROTOCOL, root.GetProperty("frontendProtocol").GetInt32());
        JsonElement parser = root.GetProperty("parser");
        Assert.False(parser.GetProperty("parseExecuted").GetBoolean());
        Assert.Contains(parser.GetProperty("status").GetString(), new[] { "missing", "unlocked", "verified" });
        if (parser.GetProperty("status").GetString() != "verified")
            Assert.Equal(JsonValueKind.Null, parser.GetProperty("deployedIdentity").ValueKind);
        Assert.Contains("ClangSharp/", parser.GetProperty("expectedIdentity").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownVersionArguments_AreRejected()
    {
        Assert.Equal(2, CliInvocation.Run(["version", "--silent"], Environment.CurrentDirectory, TextWriter.Null, TextWriter.Null));
    }
}
