using System;
using System.IO;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class ValidationCommandTests {
    [Fact]
    public void ApiSnapshot_ReflectsTheActualAssemblyAndWritesStableOutput() {
        string directory = CreateDirectory();
        try {
            string assembly = Path.Combine(AppContext.BaseDirectory, "BGCS.Core.dll");
            string snapshot = Path.Combine(directory, "snapshot.txt");
            using StringWriter error = new();
            int exitCode = CliInvocation.Run(["validate", "api-snapshot", assembly, snapshot],
                directory, TextWriter.Null, error);

            Assert.True(exitCode == 0, error.ToString());
            string first = File.ReadAllText(snapshot);
            Assert.Contains("interface BGCS.Core.Targeting.INativeTargetProvider", first);
            Assert.Equal(0, CliInvocation.Run(["validate", "api-snapshot", assembly, snapshot],
                directory, TextWriter.Null, error));
            Assert.Equal(first, File.ReadAllText(snapshot));
        }
        finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ApiSnapshotPreservesNestedTypesArrayRanksAndReferenceDirections()
    {
        string directory = CreateDirectory();
        try
        {
            string snapshot = Path.Combine(directory, "snapshot.txt");
            using var error = new StringWriter();
            int exitCode = CliInvocation.Run(["validate", "api-snapshot", typeof(ApiSnapshotFixture<>).Assembly.Location, snapshot],
                directory, TextWriter.Null, error);

            Assert.True(exitCode == 0, error.ToString());
            string content = File.ReadAllText(snapshot);
            Assert.Contains("System.Int32[,,]", content);
            Assert.Contains("BGCS.Tool.Tests.ApiSnapshotFixture+Nested<System.Int32,System.String>", content);
            Assert.Contains("ref System.Int32 value, out System.String result, in System.Int64 input", content);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ApiSnapshot_MissingAssembly_FailsWithoutCreatingSnapshot() {
        string directory = CreateDirectory();
        try {
            string snapshot = Path.Combine(directory, "snapshot.txt");
            using StringWriter error = new();
            int exitCode = CliInvocation.Run(["validate", "api-snapshot", "missing.dll", snapshot],
                directory, TextWriter.Null, error);

            Assert.Equal(2, exitCode);
            Assert.False(File.Exists(snapshot));
            Assert.NotEmpty(error.ToString());
        }
        finally {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("{\"projects\": []}", 0)]
    [InlineData("{\"projects\": [{\"id\": \"Unsafe.Package\", \"resolvedVersion\": \"1.0.0\", \"vulnerabilities\": [{\"severity\": \"High\", \"advisoryurl\": \"https://example.org/advisory\"}]}]}", 1)]
    public void DependencyAudit_ReportsActualVulnerabilities(
        string report,
        int expectedExitCode
    ) {
        string directory = CreateDirectory();
        try {
            string path = Path.Combine(directory, "report.json");
            File.WriteAllText(path, report);
            using StringWriter error = new();
            int exitCode = CliInvocation.Run(["validate", "dependencies", "vulnerabilities", path],
                directory, TextWriter.Null, error);

            Assert.Equal(expectedExitCode, exitCode);
            if (expectedExitCode != 0)
                Assert.Contains("Unsafe.Package 1.0.0: High", error.ToString());
        }
        finally {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateDirectory() {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-cli-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
