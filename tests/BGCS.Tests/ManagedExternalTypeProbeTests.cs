using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Common;
using BGCS.Contracts.Managed;
using BGCS.Infrastructure.IO;
using BGCS.Validation;
using Xunit;

namespace BGCS.Tests;

public sealed class ManagedExternalTypeProbeTests
{
    [Fact]
    public async Task DeveloperShellPlatformCannotRedirectThePrivateProbeOrChangeItsMeasuredStorage()
    {
        Dictionary<string, string?> previousPlatforms = [];
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (((string)entry.Key).Equals("Platform", StringComparison.OrdinalIgnoreCase)) previousPlatforms.Add((string)entry.Key, (string?)entry.Value);
        string[] nativeVariables = ["VCINSTALLDIR", "WindowsSdkDir", "INCLUDE", "CXX"];
        Dictionary<string, string?> nativeInputs = nativeVariables.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        string root = Path.Combine(Path.GetTempPath(), "bgcs developer shell probe " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (string name in previousPlatforms.Keys) Environment.SetEnvironmentVariable(name, null);
            Environment.SetEnvironmentVariable("Platform", "x64");
            if (!OperatingSystem.IsWindows()) Environment.SetEnvironmentVariable("pLaTfOrM", "x64");
            string observation = Path.Combine(root, "observed-child.json");
            string source = Path.Combine(root, "Consumer.cs");
            await File.WriteAllTextAsync(source, $$"""
                #if !TRACE || !DEBUG || !SHELL_PROBE
                #error Explicit symbols must append the private verifier SDK defaults.
                #endif
                using System;
                using System.Collections;
                using System.IO;
                using System.Linq;
                using System.Reflection;
                using System.Runtime.CompilerServices;
                using System.Text.Json;
                namespace Consumer;
                public struct Pair { public float X; public float Y; }
                public struct Rejected { public string X; public int Y; }
                internal static class ChildObservation {
                    [ModuleInitializer] internal static void Observe() {
                        var platforms=Environment.GetEnvironmentVariables().Keys.Cast<string>()
                            .Where(name=>name.Equals("Platform",StringComparison.OrdinalIgnoreCase)).ToArray();
                        var native=new[]{"VCINSTALLDIR","WindowsSdkDir","INCLUDE","CXX"}
                            .ToDictionary(name=>name,Environment.GetEnvironmentVariable);
                        File.WriteAllText({{JsonSerializer.Serialize(observation)}},JsonSerializer.Serialize(new {
                            location=Assembly.GetExecutingAssembly().Location, platforms, native }));
                    }
                }
                """);

            ManagedExternalTypeProbeResult result = await new ManagedExternalTypeProbe().ProbeAsync(
                [Projection("Consumer.Pair")], new() { sources = [source], defines = ["SHELL_PROBE"] }, root, TimeSpan.FromMinutes(3));

            ManagedTypeStorageFact fact = Assert.Single(result.facts);
            Assert.True(fact.isReferenceFree);
            Assert.Equal(EvidenceLevel.Verified, fact.evidence.level);
            Assert.Equal(8, fact.size);
            Assert.Equal(4, fact.alignment);
            Assert.Equal(IntPtr.Size, fact.probe.pointerSize);
            Assert.Equal(0, fact.fields.Single(field => field.name == "X").offset);
            Assert.Equal(4, fact.fields.Single(field => field.name == "Y").offset);
            Assert.All(fact.fields, field => Assert.Equal(ManagedScalarKind.BinaryFloat, field.scalarKind));
            using JsonDocument observed = JsonDocument.Parse(await File.ReadAllTextAsync(observation));
            Assert.EndsWith(Path.Combine("bin", "Debug", "net10.0", "Probe.dll"), observed.RootElement.GetProperty("location").GetString(),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Assert.Empty(observed.RootElement.GetProperty("platforms").EnumerateArray());
            foreach (string name in nativeVariables)
            {
                Assert.Equal(nativeInputs[name], observed.RootElement.GetProperty("native").GetProperty(name).GetString());
                Assert.Equal(nativeInputs[name], Environment.GetEnvironmentVariable(name));
            }
            Assert.Equal("x64", Environment.GetEnvironmentVariable("Platform"));
            if (!OperatingSystem.IsWindows()) Assert.Equal("x64", Environment.GetEnvironmentVariable("pLaTfOrM"));
            Assert.True(SourceSnapshotVerifier.Matches(result.inputs));
            Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name == "Probe");

            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => new ManagedExternalTypeProbe().ProbeAsync(
                [Projection("Consumer.Rejected")], new() { sources = [source], defines = ["SHELL_PROBE"] }, root, TimeSpan.FromMinutes(3)));
            Assert.Contains("did not compile", refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
                if (((string)entry.Key).Equals("Platform", StringComparison.OrdinalIgnoreCase)) Environment.SetEnvironmentVariable((string)entry.Key, null);
            foreach ((string name, string? value) in previousPlatforms) Environment.SetEnvironmentVariable(name, value);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ActualClosedGenericAndNumericsFieldsAreMeasuredOutsideTheHost()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs external generic " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "Consumer.cs");
            await File.WriteAllTextAsync(source, """
                #if !TRACE || !EXTERNAL_PROBE
                #error Explicit managed symbols must append the SDK default TRACE symbol.
                #endif
                using System.Runtime.InteropServices;
                namespace Consumer;
                public struct Pair<T> where T : unmanaged { public T X; public T Y; }
                [StructLayout(LayoutKind.Explicit, Size=16)]
                public struct Reordered { [FieldOffset(8)] public float X; [FieldOffset(0)] public float Y; }
                """);
            ManagedExternalTypeProbeResult result = await new ManagedExternalTypeProbe().ProbeAsync(
                [Projection("Consumer.Pair<float>"), Projection("System.Numerics.Vector2"), Projection("Consumer.Reordered")],
                new() { sources = [source], defines = ["EXTERNAL_PROBE"] }, root, TimeSpan.FromMinutes(3));
            Assert.Equal(3, result.facts.Length);
            foreach (ManagedTypeStorageFact fact in result.facts)
            {
                Assert.True(fact.isReferenceFree);
                Assert.Equal(EvidenceLevel.Verified, fact.evidence.level);
                Assert.All(fact.fields, field => { Assert.Equal(ManagedScalarKind.BinaryFloat, field.scalarKind); Assert.Equal(4, field.size); Assert.True(field.isWritable); });
                Assert.Equal(IntPtr.Size, fact.probe.pointerSize);
            }
            Assert.Equal(8, result.facts[0].size);
            Assert.Equal(4, result.facts[0].alignment);
            Assert.Equal(8, result.facts[1].size);
            Assert.Equal(16, result.facts[2].size);
            Assert.Equal(8, result.facts[2].fields.Single(field => field.name == "X").offset);
            Assert.Contains(result.inputs.files, input => input.path == source);
            Assert.True(SourceSnapshotVerifier.Matches(result.inputs));
            Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name == "Probe");
            await File.AppendAllTextAsync(source, "\n// changed after measurement\n");
            Assert.False(SourceSnapshotVerifier.Matches(result.inputs));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("public struct Rejected { public string X; public int Y; }")]
    [InlineData("public struct Rejected { public readonly int X; public int Y; }")]
    [InlineData("public struct Rejected { public int X; private int Y; }")]
    [InlineData("public struct Rejected { public int X; public int Y; public int Unmapped; }")]
    public async Task ReferenceReadonlyPrivateAndUnmappedFieldsAreRejected(string declaration)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs external refused " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "Consumer.cs");
            await File.WriteAllTextAsync(source, "namespace Consumer; " + declaration);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new ManagedExternalTypeProbe().ProbeAsync(
                [Projection("Consumer.Rejected")], new() { sources = [source] }, root, TimeSpan.FromMinutes(3)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CallerCancellationDoesNotBecomeAStorageReceipt()
    {
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ManagedExternalTypeProbe().ProbeAsync(
            [Projection("System.Numerics.Vector2")], new(), Path.GetTempPath(), TimeSpan.FromMinutes(1), canceled.Token));
    }

    private static ExternalTypeProjection Projection(string type) => new(new("point"), type, [new(new("x"), "X"), new(new("y"), "Y")]);
}
