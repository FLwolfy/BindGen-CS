using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Infrastructure.Execution;
using Xunit;

namespace BGCS.Tests;

public sealed class EmbeddedWorkerGenerationTests
{
    [Fact]
    public async Task FreshEmbeddedHost_GeneratesUsingRealWorkerWithoutLoadingTheClangFrontend()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string repository = directory?.FullName ?? throw new InvalidOperationException("The repository root is unavailable.");
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        string worker = Path.Combine(repository, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
        string host = Path.Combine(repository, "tests", "fixtures", "hosts", "EmbeddedSdkHost", "bin", configuration, "net10.0", "EmbeddedSdkHost.dll");
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-embedded-worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(temporary, "native.h"), "struct Item { char prefix; int value; }; int read_item(const struct Item* item); void notify(void);");
            string input = Path.Combine(temporary, "bindgen.json");
            await File.WriteAllTextAsync(input, "{\"schemaVersion\":1,\"library\":{\"id\":\"example\",\"nativeName\":\"example\"},\"source\":{\"language\":\"c\",\"headers\":[\"native.h\"]},\"api\":{\"namespace\":\"Generated.Example\",\"name\":\"NativeApi\"},\"cache\":{\"enabled\":false}}");
            string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            string dotnet = string.IsNullOrEmpty(root) ? "dotnet" : Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            var result = await ProcessExecutor.ExecuteAsync(dotnet, [host, worker, input], temporary, TimeSpan.FromSeconds(60));
            Assert.True(result.exitCode == 0, result.standardOutput + Environment.NewLine + result.standardError);
            using JsonDocument output = JsonDocument.Parse(result.standardOutput.Trim());
            Assert.True(output.RootElement.GetProperty("success").GetBoolean(), output.RootElement.GetProperty("diagnosticSummary").GetString());
            Assert.False(output.RootElement.GetProperty("frontendLoaded").GetBoolean());
            Assert.True(Directory.Exists(Path.Combine(temporary, "Generated")));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
