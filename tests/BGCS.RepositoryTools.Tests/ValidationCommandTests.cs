using System;
using System.IO;
using Xunit;

namespace BGCS.RepositoryTools.Tests;

public sealed class ValidationCommandTests
{
    [Fact]
    public void ApiSnapshot_ReflectsTheActualAssemblyAndWritesStableOutput()
    {
        string directory = CreateDirectory();
        try
        {
            string assembly = Path.Combine(AppContext.BaseDirectory, "BGCS.Core.dll");
            string snapshot = Path.Combine(directory, "snapshot.txt");
            using StringWriter error = new();
            int exitCode = CliInvocation.Run(["validate", "api-snapshot", assembly, snapshot],
                directory, TextWriter.Null, error);

            Assert.True(exitCode == 0, error.ToString());
            string first = File.ReadAllText(snapshot);
            Assert.Contains("interface BGCS.Core.Targets.INativeTargetProvider", first);
            Assert.Equal(0, CliInvocation.Run(["validate", "api-snapshot", assembly, snapshot],
                directory, TextWriter.Null, error));
            Assert.Equal(first, File.ReadAllText(snapshot));
        }
        finally
        {
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
            Assert.Contains("BGCS.RepositoryTools.Tests.ApiSnapshotFixture+Nested<System.Int32,System.String>", content);
            Assert.Contains("ref System.Int32 value, out System.String result, in System.Int64 input", content);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ApiSnapshotPreservesCompiledFunctionPointerSignaturesAndCallingConventions()
    {
        string directory = CreateDirectory();
        try
        {
            string snapshot = Path.Combine(directory, "snapshot.txt");
            using StringWriter error = new();
            int exitCode = CliInvocation.Run(["validate", "api-snapshot", typeof(FunctionPointerSnapshotFixture).Assembly.Location, snapshot],
                directory, TextWriter.Null, error);
            Assert.True(exitCode == 0, error.ToString());
            string content = File.ReadAllText(snapshot);
            Assert.Contains("delegate* unmanaged[System.Runtime.CompilerServices.CallConvCdecl]<System.Int32,System.Single,System.Double> cdecl", content);
            Assert.Contains("delegate* unmanaged[System.Runtime.CompilerServices.CallConvStdcall]<System.Byte*,System.Int64> stdcall", content);
            Assert.Contains("delegate* managed<System.Int32,System.Int32> managed", content);
            Assert.Contains("ctor(delegate* unmanaged[System.Runtime.CompilerServices.CallConvStdcall]<System.Byte*,System.Int64> callback)", content);
            Assert.Contains("property delegate* unmanaged[System.Runtime.CompilerServices.CallConvCdecl]<System.Int32,System.Single,System.Double> callback { get; set }", content);
            Assert.Contains("field delegate* managed<T,T> generic", content);
            Assert.Contains("method delegate* managed<TValue,TValue> Identity(delegate* managed<TValue,TValue> callback)", content);
            Assert.Contains("delegate* unmanaged[System.Runtime.CompilerServices.CallConvCdecl]<System.Void> nested(delegate* unmanaged[System.Runtime.CompilerServices.CallConvCdecl]<System.Int32,System.Int32>* input)", content);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ApiSnapshotCapturesLiteralIdentityInheritanceConstraintsAndCallableShapeWithoutRunningCode()
    {
        string directory = CreateDirectory();
        try
        {
            string snapshot = Path.Combine(directory, "surface.txt");
            using StringWriter error = new();
            Assert.True(CliInvocation.Run(["validate", "api-snapshot", typeof(ApiSurfaceFixture<>).Assembly.Location, snapshot],
                directory, TextWriter.Null, error) == 0, error.ToString());
            string text = File.ReadAllText(snapshot);
            Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
            Assert.EndsWith("\n", text, StringComparison.Ordinal);
            Assert.Contains("underlying=System.UInt64", text);
            Assert.Contains("High [static; const=9223372036854775808]", text);
            Assert.Contains("All [static; const=18446744073709551615]", text);
            Assert.Contains("Maximum [static; const=18446744073709551615]", text);
            Assert.Contains("Exact [static; readonly; const=-12.50]", text);
            Assert.Contains("NegativeZero [static; const=-0:0x8000000000000000]", text);
            Assert.Contains("base=BGCS.RepositoryTools.Tests.ApiSurfaceBase", text);
            Assert.Contains("interfaces=System.IDisposable", text);
            Assert.Contains("generic T=ReferenceTypeConstraint, DefaultConstructorConstraint:System.IDisposable", text);
            Assert.Contains("unmanaged TValue", text);
            Assert.Contains("property System.String label { get; init }", text);
            Assert.Contains("property System.Int32 Item[System.Int32 index] { get }", text);
            Assert.Contains("System.Int32 count [optional; default=17]) [static]", text);
            Assert.Contains("System.Int32[] values [params]", text);
            Assert.Contains("op_Implicit(BGCS.RepositoryTools.Tests.ApiSurfaceFixture<T> value) [static]", text);
            Assert.Contains("UninitializedProbe [static; readonly]", text);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ApiSnapshot_MissingAssembly_FailsWithoutCreatingSnapshot()
    {
        string directory = CreateDirectory();
        try
        {
            string snapshot = Path.Combine(directory, "snapshot.txt");
            using StringWriter error = new();
            int exitCode = CliInvocation.Run(["validate", "api-snapshot", "missing.dll", snapshot],
                directory, TextWriter.Null, error);

            Assert.Equal(2, exitCode);
            Assert.False(File.Exists(snapshot));
            Assert.NotEmpty(error.ToString());
        }
        finally
        {
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
        try
        {
            string path = Path.Combine(directory, "report.json");
            File.WriteAllText(path, report);
            using StringWriter error = new();
            int exitCode = CliInvocation.Run(["validate", "dependencies", "vulnerabilities", path],
                directory, TextWriter.Null, error);

            Assert.Equal(expectedExitCode, exitCode);
            if (expectedExitCode != 0)
                Assert.Contains("Unsafe.Package 1.0.0: High", error.ToString());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-cli-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
