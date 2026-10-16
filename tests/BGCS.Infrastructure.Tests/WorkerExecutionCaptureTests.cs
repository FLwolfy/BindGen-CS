using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class WorkerExecutionCaptureTests
{
    [Theory]
    [InlineData("locale\\resource.bin")]
    [InlineData("locale/RESOURCE.bin")]
    public void PortableManifestSpellingUsesTheActualPreflightFilePath(string declaredPath)
    {
        using Fixture fixture = new();
        string actual = Path.GetFullPath(fixture.PathOf("locale/resource.bin"));
        Directory.CreateDirectory(Path.GetDirectoryName(actual)!);
        byte[] bytes = Encoding.UTF8.GetBytes("independent localized resource");
        File.WriteAllBytes(actual, bytes);
        ContentDigest digest = new(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        fixture.Manifest = fixture.Manifest with { artifacts = fixture.Manifest.artifacts.Add(new(declaredPath, "application/octet-stream", digest)) };
        fixture.WriteManifest();
        new WorkerAssetDeployer().VerifyDeployment(fixture.Source, fixture.Manifest, fixture.Rid);
        var captured = Capture(fixture, fixture.Extras());
        InputDependency resource = Assert.Single(captured.Snapshot.files, file => file.path == actual);
        Assert.Equal(digest, resource.digest);
        Assert.Equal(bytes.Length, resource.length);
        Assert.True(SourceSnapshotVerifier.Matches(captured.Snapshot));
    }

    [Theory]
    [InlineData(false, "utf8")]
    [InlineData(true, "utf8")]
    [InlineData(true, "utf16le")]
    [InlineData(true, "utf16be")]
    [InlineData(true, "utf32le")]
    [InlineData(true, "utf32be")]
    public void CapturedJsonPreservesStreamBomAndReadAllTextEncodingSemantics(bool manifestBom, string encodingName)
    {
        using Fixture fixture = new();
        Encoding encoding = encodingName switch
        {
            "utf8" => new UTF8Encoding(true),
            "utf16le" => Encoding.Unicode,
            "utf16be" => Encoding.BigEndianUnicode,
            "utf32le" => new UTF32Encoding(false, true),
            _ => new UTF32Encoding(true, true)
        };
        foreach (string relative in new[] { Fixture.Dependencies, Fixture.Configuration })
            fixture.Replace(relative, encoding.GetPreamble().Concat(encoding.GetBytes(File.ReadAllText(fixture.PathOf(relative)))).ToArray());
        fixture.WriteManifest(manifestBom);
        var captured = Capture(fixture, fixture.Extras());
        Assert.Equal(JsonSerializer.Serialize(fixture.Manifest), JsonSerializer.Serialize(captured.Manifest));
        Assert.Equal(File.ReadAllText(fixture.PathOf(Fixture.Configuration)), captured.RuntimeConfiguration);
        Assert.True(SourceSnapshotVerifier.Matches(captured.Snapshot));
        Assert.Contains(captured.Snapshot.files, input => input.path == fixture.Host);
        Assert.Contains(captured.Snapshot.files, input => input.path == Path.Combine(fixture.Runtime, "core.dll"));
        Assert.Contains(captured.Snapshot.files, input => input.path == Path.Combine(fixture.Fxr, "resolver"));
        Assert.Contains(captured.Snapshot.searchDirectories, directory => directory.path == fixture.Runtime);
        Assert.Contains(captured.Snapshot.searchDirectories, directory => directory.path == fixture.Fxr);
        File.WriteAllText(Path.Combine(fixture.Runtime, "new.dll"), "new runtime member");
        Assert.False(SourceSnapshotVerifier.Matches(captured.Snapshot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonUtf8ManifestRefusalsStillMatchThePublicStreamDeserializer(bool utf32)
    {
        using Fixture fixture = new();
        File.WriteAllText(fixture.PathOf("worker-assets.manifest.json"), JsonSerializer.Serialize(fixture.Manifest),
            utf32 ? new UTF32Encoding(false, true) : Encoding.Unicode);
        Exception? original = Record.Exception(() => new WorkerAssetDeployer().ReadManifest(fixture.PathOf("worker-assets.manifest.json")));
        Exception? joint = Record.Exception(() => Capture(fixture, fixture.Extras()));
        Assert.NotNull(original);
        Assert.NotNull(joint);
        Assert.Equal(original.GetType(), joint.GetType());
    }

    [Theory]
    [InlineData("host")]
    [InlineData("runtime")]
    [InlineData("fxr")]
    public void EveryAdditionalExecutionClosureRetainsItsIndependentByteVerification(string kind)
    {
        using Fixture fixture = new();
        var captured = Capture(fixture, fixture.Extras());
        string path = kind == "host" ? fixture.Host : kind == "runtime" ? Path.Combine(fixture.Runtime, "core.dll") : Path.Combine(fixture.Fxr, "resolver");
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        byte[] changed = File.ReadAllBytes(path);
        changed[0] ^= 1;
        File.WriteAllBytes(path, changed);
        File.SetLastWriteTimeUtc(path, timestamp);
        Assert.False(SourceSnapshotVerifier.Matches(captured.Snapshot));
    }

    [Fact]
    public void ReservedPublicationOwnershipMetadataKeepsItsExistingNonExecutionRole()
    {
        using Fixture fixture = new();
        string owner = fixture.PathOf(".bgcs-artifacts.json");
        File.WriteAllText(owner, "{}");
        new WorkerAssetDeployer().VerifyDeployment(fixture.Source, fixture.Manifest, fixture.Rid);
        var captured = Capture(fixture, fixture.Extras());
        Assert.DoesNotContain(captured.Snapshot.files, file => file.path == owner);
        Assert.True(SourceSnapshotVerifier.Matches(captured.Snapshot));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("case-collision")]
    [InlineData("escape")]
    [InlineData("reserved")]
    public void JointCaptureAndFreshPublicVerificationRejectTheSameInvalidSets(string damage)
    {
        using Fixture fixture = new();
        switch (damage)
        {
            case "missing": File.Delete(fixture.PathOf("BGCS.Contracts.dll")); break;
            case "extra": File.WriteAllText(fixture.PathOf("unlisted.dll"), "unlisted"); break;
            case "case-collision":
                fixture.Manifest = fixture.Manifest with { artifacts = fixture.Manifest.artifacts.Add(fixture.Manifest.artifacts[0] with
                { relativePath = fixture.Manifest.artifacts[0].relativePath.ToUpperInvariant() }) };
                break;
            case "escape":
            case "reserved":
                fixture.Manifest = fixture.Manifest with { artifacts = fixture.Manifest.artifacts.Add(new(
                    damage == "escape" ? "../outside.dll" : "worker-assets.manifest.json", "application/json", new(new string('0', 64)))) };
                break;
        }
        fixture.WriteManifest();
        Assert.Throws<IOException>(() => new WorkerAssetDeployer().VerifyDeployment(fixture.Source, fixture.Manifest, fixture.Rid));
        bool enumerated = false;
        Assert.Throws<IOException>(() => Capture(fixture, Change(() => enumerated = true, fixture.Extras())));
        Assert.False(enumerated);
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("dependencies")]
    [InlineData("runtime-configuration")]
    public void ParsedJsonMustBeTheExactBytesLaterCapturedEvenWhenBothVersionsAreValid(string input)
    {
        using Fixture fixture = new();
        string relative = input == "manifest" ? "worker-assets.manifest.json" : input == "dependencies" ? Fixture.Dependencies : Fixture.Configuration;
        byte[] next = File.ReadAllBytes(fixture.PathOf(relative)).Concat(new byte[] { (byte)' ' }).ToArray();
        if (input != "manifest") fixture.Replace(relative, next, write: false);
        IOException failure = Assert.Throws<IOException>(() => Capture(fixture,
            Change(() => File.WriteAllBytes(fixture.PathOf(relative), next), fixture.Extras())));
        Assert.Contains("between reading and snapshot capture", failure.Message, StringComparison.Ordinal);
        Assert.True(SourceSnapshotVerifier.Matches(Capture(fixture, fixture.Extras()).Snapshot));
    }

    [Fact]
    public void FreshCapturedBytesCannotBeAcceptedAgainstAnOlderManifestDigest()
    {
        using Fixture fixture = new();
        string path = fixture.PathOf("BGCS.Contracts.dll");
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        byte[] changed = File.ReadAllBytes(path);
        changed[0] ^= 1;
        IOException failure = Assert.Throws<IOException>(() => Capture(fixture, Change(() =>
        {
            File.WriteAllBytes(path, changed);
            File.SetLastWriteTimeUtc(path, timestamp);
        }, fixture.Extras())));
        Assert.Contains("digest mismatch", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LaterMutationInvalidatesTheReturnedSnapshotAndBothPublicVerificationEntrypointsStayFresh()
    {
        using Fixture fixture = new();
        var captured = Capture(fixture, fixture.Extras());
        new WorkerAssetDeployer().VerifyDeployment(fixture.Source, fixture.Manifest, fixture.Rid);
        Assert.Equal(fixture.PathOf("BGCS.Frontend.Clang.Worker.dll"), WorkerAssetLocator.Locate(fixture.Source));
        string path = fixture.PathOf("BGCS.Contracts.dll");
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        byte[] bytes = File.ReadAllBytes(path);
        bytes[0] ^= 1;
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, timestamp);
        Assert.False(SourceSnapshotVerifier.Matches(captured.Snapshot));
        Assert.Contains("digest mismatch", Assert.Throws<IOException>(() =>
            new WorkerAssetDeployer().VerifyDeployment(fixture.Source, fixture.Manifest, fixture.Rid)).Message, StringComparison.Ordinal);
        Assert.Contains("digest mismatch", Assert.Throws<IOException>(() => WorkerAssetLocator.Locate(fixture.Source)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DescendantLinksAreRejectedBeforeReadsAndWhenCreatedDuringCapture(bool late)
    {
        using Fixture fixture = new();
        string alias = fixture.PathOf("linked");
        bool enumerated = false;
        if (!late) fixture.CreateAlias(alias, fixture.Runtime);
        IOException failure = Assert.Throws<IOException>(() => Capture(fixture, Change(() =>
        {
            enumerated = true;
            if (late) fixture.CreateAlias(alias, fixture.Runtime);
        }, fixture.Extras())));
        Assert.Contains("Linked artifact", failure.Message, StringComparison.Ordinal);
        Assert.Equal(late, enumerated);
    }

    [Fact]
    public void ALinkedOwnedLeafIsRejectedAndAnAllowedParentAliasCannotRetargetDuringCapture()
    {
        using Fixture fixture = new();
        string leaf = Path.Combine(fixture.Root, "worker-link");
        fixture.CreateAlias(leaf, fixture.Source);
        Assert.Throws<IOException>(() => Capture(fixture, fixture.Extras(), root: leaf));
        string alias = Path.Combine(fixture.Root, "parent-alias");
        fixture.CreateAlias(alias, Path.GetDirectoryName(fixture.Source)!);
        string logical = Path.Combine(alias, "worker");
        Assert.True(SourceSnapshotVerifier.Matches(Capture(fixture, fixture.Extras(), root: logical).Snapshot));
        string other = Path.Combine(fixture.Root, "other");
        Directory.CreateDirectory(Path.Combine(other, "worker"));
        foreach (string file in Directory.EnumerateFiles(fixture.Source, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(other, "worker", Path.GetRelativePath(fixture.Source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        Assert.Throws<IOException>(() => Capture(fixture, Change(() =>
        {
            Directory.Delete(alias);
            fixture.CreateAlias(alias, other);
        }, fixture.Extras()), root: logical));
    }

    [Fact]
    public void CancellationRetainsTheCallerTokenBeforeExtraInputsAreEnumerated()
    {
        using Fixture fixture = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool enumerated = false;
        OperationCanceledException failure = Assert.ThrowsAny<OperationCanceledException>(() => Capture(fixture,
            Change(() => enumerated = true, fixture.Extras()), cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.False(enumerated);
    }

    private static (WorkerAssetManifest Manifest, SourceSnapshot Snapshot, string RuntimeConfiguration) Capture(
        Fixture fixture, IEnumerable<DeclaredExternalInput> extras, CancellationToken cancellationToken = default, string? root = null)
    {
        MethodInfo method = typeof(WorkerAssetDeployer).GetMethod("CaptureVerifiedDeployment", BindingFlags.NonPublic | BindingFlags.Instance)!;
        string source = root ?? fixture.Source;
        try
        {
            return ((WorkerAssetManifest, SourceSnapshot, string))method.Invoke(new WorkerAssetDeployer(),
                [source, fixture.Rid, Path.Combine(source, Fixture.Configuration), extras, cancellationToken])!;
        }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(failure.InnerException).Throw();
            throw;
        }
    }

    private static IEnumerable<DeclaredExternalInput> Change(Action action, IEnumerable<DeclaredExternalInput> inputs)
    {
        action();
        foreach (DeclaredExternalInput input in inputs) yield return input;
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Dependencies = "BGCS.Frontend.Clang.Worker.deps.json";
        internal const string Configuration = "BGCS.Frontend.Clang.Worker.runtimeconfig.json";
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-worker-capture-" + Guid.NewGuid().ToString("N"));
        internal string Source => Path.Combine(Root, "original", "worker");
        internal string Runtime => Path.Combine(Root, "runtime");
        internal string Fxr => Path.Combine(Root, "fxr");
        internal string Host => Path.Combine(Root, "dotnet-host");
        internal string Rid => RuntimeInformation.RuntimeIdentifier;
        internal WorkerAssetManifest Manifest { get; set; }
        private readonly HashSet<string> m_aliases = new(StringComparer.Ordinal);
        internal Fixture()
        {
            Directory.CreateDirectory(Source);
            Directory.CreateDirectory(Runtime);
            Directory.CreateDirectory(Fxr);
            File.WriteAllText(Host, "selected host");
            File.WriteAllText(Path.Combine(Runtime, "core.dll"), "selected runtime");
            File.WriteAllText(Path.Combine(Fxr, "resolver"), "selected fxr");
            string extension = Rid.StartsWith("win-", StringComparison.Ordinal) ? ".dll" : Rid.StartsWith("osx-", StringComparison.Ordinal) ? ".dylib" : ".so";
            string[] names = ["BGCS.Frontend.Clang.Worker.dll", Dependencies, Configuration, "BGCS.Contracts.dll", "BGCS.Infrastructure.dll",
                "BGCS.Frontend.Clang.dll", "ClangSharp.dll", "ClangSharp.Interop.dll", "runtimes/" + Rid + "/native/libclang" + extension,
                "runtimes/" + Rid + "/native/libClangSharp" + extension];
            Manifest = new(1, Rid, WorkerProtocol.C_PARSER_IDENTITY, names.Select(name =>
            {
                string text = name == Dependencies ? "{\"runtimeTarget\":{\"name\":\"net10\"},\"targets\":{\"net10\":{}}}"
                    : name == Configuration ? "{\"runtimeOptions\":{\"framework\":{\"name\":\"Microsoft.NETCore.App\"},\"marker\":\"café\"}}" : "fixture:" + name;
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                Directory.CreateDirectory(Path.GetDirectoryName(PathOf(name))!);
                File.WriteAllBytes(PathOf(name), bytes);
                return Descriptor(name, bytes);
            }).ToImmutableArray());
            WriteManifest();
        }
        internal string PathOf(string relative) => Path.Combine(Source, relative);
        internal void WriteManifest(bool bom = false) => File.WriteAllText(PathOf("worker-assets.manifest.json"),
            JsonSerializer.Serialize(Manifest), new UTF8Encoding(bom));
        internal void Replace(string relative, byte[] bytes, bool write = true)
        {
            if (write) File.WriteAllBytes(PathOf(relative), bytes);
            Manifest = Manifest with { artifacts = Manifest.artifacts.Select(artifact => artifact.relativePath == relative ? Descriptor(relative, bytes) : artifact).ToImmutableArray() };
            WriteManifest();
        }
        private static ArtifactDescriptor Descriptor(string relative, byte[] bytes) =>
            new(relative, "application/octet-stream", new(Convert.ToHexStringLower(SHA256.HashData(bytes))));
        internal IEnumerable<DeclaredExternalInput> Extras() =>
            [new(Host, ExternalInputKind.File), new(Runtime, ExternalInputKind.Directory), new(Fxr, ExternalInputKind.Directory)];
        internal void CreateAlias(string path, string target)
        {
            Assert.StartsWith(Root + Path.DirectorySeparatorChar, Path.GetFullPath(path), StringComparison.Ordinal);
            Assert.StartsWith(Root + Path.DirectorySeparatorChar, Path.GetFullPath(target), StringComparison.Ordinal);
            if (OperatingSystem.IsWindows())
            {
                string command = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + path.Replace("'", "''", StringComparison.Ordinal)
                    + "' -Target '" + target.Replace("'", "''", StringComparison.Ordinal) + "' | Out-Null";
                ProcessStartInfo start = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) })
                    start.ArgumentList.Add(argument);
                using Process process = Process.Start(start)!;
                if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); throw new TimeoutException("Owned junction creation timed out."); }
                Assert.True(process.ExitCode == 0, process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd());
            }
            else Directory.CreateSymbolicLink(path, target);
            m_aliases.Add(path);
            Assert.NotNull(new DirectoryInfo(path).LinkTarget);
        }
        public void Dispose()
        {
            foreach (string alias in m_aliases.OrderByDescending(path => path.Length))
                if (new DirectoryInfo(alias).LinkTarget is not null) Directory.Delete(alias);
            Directory.Delete(Root, recursive: true);
        }
    }
}
