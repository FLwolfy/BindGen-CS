using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace BGCS.RepositoryTools.Tests;

public sealed class DeliveredDependencySupplyChainTests
{
    [Fact]
    public void ActualArchivesProvideTransitiveDependenciesLicensesHashesAndProvenance()
    {
        using Fixture fixture = new();
        fixture.WritePackage();
        Assert.Equal(0, fixture.Run());
        using JsonDocument sbom = fixture.Read("sbom.spdx.json");
        JsonElement[] packages = sbom.RootElement.GetProperty("packages").EnumerateArray().ToArray();
        JsonElement alpha = Assert.Single(packages, package => package.GetProperty("name").GetString() == "Alpha");
        JsonElement beta = Assert.Single(packages, package => package.GetProperty("name").GetString() == "Beta");
        Assert.Equal("MIT", alpha.GetProperty("licenseDeclared").GetString());
        Assert.Equal("Apache-2.0", beta.GetProperty("licenseDeclared").GetString());
        Assert.Equal(Convert.ToHexString(Fixture.AlphaHash).ToLowerInvariant(), alpha.GetProperty("checksums")[0].GetProperty("checksumValue").GetString());
        Assert.Contains(sbom.RootElement.GetProperty("relationships").EnumerateArray(), edge =>
            edge.GetProperty("spdxElementId").GetString() == alpha.GetProperty("SPDXID").GetString()
            && edge.GetProperty("relationshipType").GetString() == "DEPENDS_ON"
            && edge.GetProperty("relatedSpdxElement").GetString() == beta.GetProperty("SPDXID").GetString());
        JsonElement file = Assert.Single(sbom.RootElement.GetProperty("files").EnumerateArray(), item => item.GetProperty("fileName").GetString()!.EndsWith("/Alpha.dll", StringComparison.Ordinal));
        Assert.Equal(Fixture.Hash("alpha executable bytes"), file.GetProperty("checksums")[0].GetProperty("checksumValue").GetString());
        using JsonDocument provenance = fixture.Read("provenance.slsa.json");
        Assert.Equal(Fixture.FileHash(fixture.Package), provenance.RootElement.GetProperty("subject")[0].GetProperty("digest").GetProperty("sha256").GetString());
        Assert.Contains(provenance.RootElement.GetProperty("predicate").GetProperty("buildDefinition").GetProperty("resolvedDependencies").EnumerateArray(), item => item.GetProperty("uri").GetString() == "pkg:nuget/Beta@2.0.0");
        Assert.EndsWith("/.github/workflows/publish-bgcs-runtime-nuget.yml", provenance.RootElement.GetProperty("predicate").GetProperty("runDetails").GetProperty("builder").GetProperty("id").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing-license", "license/checksum")]
    [InlineData("missing-edge", "incomplete")]
    [InlineData("wrong-checksum", "license/checksum")]
    [InlineData("wrong-license", "contradicts")]
    [InlineData("changed-notice", "audited hash")]
    [InlineData("missing-assembly", "missing its managed asset")]
    [InlineData("unresolved-product", "no exact accepted")]
    public void MissingOrContradictoryDeliveredEvidenceFailsBeforeWriting(string mutation, string reason)
    {
        using Fixture fixture = new();
        fixture.WritePackage(mutation);
        Assert.Equal(2, fixture.Run());
        Assert.Contains(reason, fixture.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(fixture.Output, "sbom.spdx.json")));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "provenance.slsa.json")));
    }

    [Fact]
    public void DuplicateArchiveEntriesCannotSelectAnArbitraryHash()
    {
        using Fixture fixture = new();
        fixture.WritePackage("duplicate");
        Assert.Equal(2, fixture.Run());
        Assert.Contains("duplicate entries", fixture.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RelocatedNativeComponentEdgesUseActualFilesLicenseTextAndPinnedSourceMaterials()
    {
        using Fixture fixture = new();
        fixture.WriteNativePackage();
        Assert.Equal(0, fixture.Run());
        using JsonDocument sbom = fixture.Read("sbom.spdx.json");
        JsonElement[] packages = sbom.RootElement.GetProperty("packages").EnumerateArray().ToArray();
        JsonElement llvm = Assert.Single(packages, package => package.GetProperty("name").GetString() == "LLVM");
        JsonElement clang = Assert.Single(packages, package => package.GetProperty("name").GetString() == "ClangSharp");
        string licenseId = llvm.GetProperty("licenseDeclared").GetString()!;
        Assert.StartsWith("LicenseRef-", licenseId, StringComparison.Ordinal);
        Assert.Contains(sbom.RootElement.GetProperty("hasExtractedLicensingInfos").EnumerateArray(), item =>
            item.GetProperty("licenseId").GetString() == licenseId && item.GetProperty("extractedText").GetString() == "actual LLVM license text\n");
        Assert.Contains(sbom.RootElement.GetProperty("relationships").EnumerateArray(), edge =>
            edge.GetProperty("spdxElementId").GetString() == clang.GetProperty("SPDXID").GetString()
            && edge.GetProperty("relationshipType").GetString() == "DEPENDS_ON"
            && edge.GetProperty("relatedSpdxElement").GetString() == llvm.GetProperty("SPDXID").GetString());
        using JsonDocument provenance = fixture.Read("provenance.slsa.json");
        Assert.Contains(provenance.RootElement.GetProperty("predicate").GetProperty("buildDefinition").GetProperty("resolvedDependencies").EnumerateArray(), item =>
            item.GetProperty("uri").GetString() == "git+https://github.com/dotnet/ClangSharp.git"
            && item.GetProperty("digest").GetProperty("gitCommit").GetString() == new string('c', 40));
    }

    [Theory]
    [InlineData("missing-native-edge", "native dependency graph is incomplete")]
    [InlineData("missing-native-owner", "no component/license provenance")]
    [InlineData("wrong-native-license", "audited hash")]
    [InlineData("wrong-native-bytes", "matching delivered binary")]
    public void NativeSourceLicenseAndDependencyGraphsMustMatchTheActualArchive(string mutation, string reason)
    {
        using Fixture fixture = new();
        fixture.WriteNativePackage(mutation);
        Assert.Equal(2, fixture.Run());
        Assert.Contains(reason, fixture.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(fixture.Output, "sbom.spdx.json")));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-hash")]
    [InlineData("missing-archive")]
    [InlineData("extra-archive")]
    [InlineData("unstable-source")]
    [InlineData("dirty-source")]
    [InlineData("wrong-revision")]
    public void AcceptedInventoryMustBindTheCompleteActualPackageSetBeforeAnyOutput(string mutation)
    {
        using Fixture fixture = new();
        fixture.WritePackage();
        Assert.Equal(2, fixture.Run(mutation));
        Assert.Contains("package", fixture.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(fixture.Output, "sbom.spdx.json")));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "provenance.slsa.json")));
    }

    [Fact]
    public void EmbeddedVocabularySbomUsesActualPeBytesAndPinnedTransformedSource()
    {
        using Fixture fixture = new();
        fixture.WriteEmbeddedResourcePackage();
        Assert.Equal(0, fixture.Run());
        using JsonDocument sbom = fixture.Read("sbom.spdx.json");
        JsonElement resource = Assert.Single(sbom.RootElement.GetProperty("packages").EnumerateArray(), value => value.GetProperty("name").GetString() == "dwyl.english-words");
        Assert.Equal("Unlicense", resource.GetProperty("licenseDeclared").GetString());
        Assert.Equal("NOASSERTION", resource.GetProperty("licenseConcluded").GetString());
        Assert.Equal("480a91457cb2e2e11226f0f37d91e8bfce2d9c02825e02a0b8d2de2a70a45bcc", Assert.Single(resource.GetProperty("checksums").EnumerateArray()).GetProperty("checksumValue").GetString());
        Assert.Contains(sbom.RootElement.GetProperty("files").EnumerateArray(), value => value.GetProperty("fileName").GetString()!.Contains("!resource/BGCS.CSharp.Naming.en_EN.txt", StringComparison.Ordinal));
        using JsonDocument provenance = fixture.Read("provenance.slsa.json");
        Assert.Contains(provenance.RootElement.GetProperty("predicate").GetProperty("buildDefinition").GetProperty("resolvedDependencies").EnumerateArray(), value =>
            value.GetProperty("uri").GetString()!.Contains("7995690ebca6e7d5e1d56ad6cb1b446a358b2b79/words_alpha.txt", StringComparison.Ordinal)
            && value.GetProperty("digest").GetProperty("sha256").GetString() == "fe1f2a9bf0d872f1c8367fc349e4a6f8e8300b06b6294ea5167483b2bc496127");
    }

    [Theory]
    [InlineData("missing-inventory", "no embedded-resource provenance")]
    [InlineData("unrelated-owner-inventory", "no embedded-resource provenance")]
    [InlineData("missing-owner", "no delivered owner")]
    [InlineData("missing-license", "audited hash")]
    [InlineData("wrong-license", "audited hash")]
    [InlineData("wrong-payload", "PE payload digest")]
    [InlineData("wrong-source", "pinned upstream bytes")]
    [InlineData("unknown-transform", "noncanonical transformation")]
    [InlineData("wrong-resource", "resource is absent")]
    [InlineData("unknown-schema", "schema is unsupported")]
    [InlineData("malformed-owner", "PE embedded-resource metadata is invalid")]
    [InlineData("resource-section-size", "actual PE resource section")]
    public void EmbeddedResourceInventoryCannotInventSourceLicenseOrPayloadProof(string mutation, string reason)
    {
        using Fixture fixture = new();
        fixture.WriteEmbeddedResourcePackage(mutation);
        Assert.Equal(2, fixture.Run());
        Assert.Contains(reason, fixture.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(fixture.Output, "sbom.spdx.json")));
    }

    private sealed class Fixture : IDisposable
    {
        internal static readonly byte[] AlphaHash = Enumerable.Repeat((byte)0x41, 64).ToArray();
        private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-delivered-sbom-" + Guid.NewGuid().ToString("N"));
        internal string Output => Path.Combine(m_root, "attestations");
        internal string Package => Path.Combine(m_root, "packages", "BGCS.3.0.0.nupkg");
        internal StringWriter Error { get; } = new();

        internal Fixture() => Directory.CreateDirectory(Path.GetDirectoryName(Package)!);

        internal void WriteEmbeddedResourcePackage(string? mutation = null)
        {
            DirectoryInfo? root = new(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
            string notices = Path.Combine(root?.FullName ?? throw new InvalidOperationException("Repository inputs unavailable."), "distribution", "third-party", "english-words");
            JsonObject proof = JsonNode.Parse(File.ReadAllText(Path.Combine(notices, "embedded-resource-provenance.json")))!.AsObject();
            if (mutation == "unrelated-owner-inventory") proof["assembly"] = "BGCS.Alternate.dll";
            if (mutation == "wrong-payload") proof["sha256"] = new string('0', 64);
            if (mutation == "wrong-source") proof["sourceSha256"] = new string('0', 64);
            if (mutation == "unknown-transform") proof["transformation"] = "unspecified";
            if (mutation == "wrong-resource") proof["resource"] = "absent.resource";
            if (mutation == "unknown-schema") proof["schemaVersion"] = 99;
            using ZipArchive archive = ZipFile.Open(Package, ZipArchiveMode.Create);
            void Add(string path, byte[] bytes) { using Stream stream = archive.CreateEntry(path).Open(); stream.Write(bytes); }
            Add("BGCS.nuspec", Encoding.UTF8.GetBytes("<package><metadata><id>BGCS</id><version>3.0.0</version><license type=\"expression\">MIT</license></metadata></package>"));
            if (mutation != "missing-owner")
            {
                byte[] image = File.ReadAllBytes(typeof(BGCS.CSharp.CSharpBackend).Assembly.Location);
                if (mutation == "malformed-owner") image = Encoding.UTF8.GetBytes("not a PE assembly");
                if (mutation == "resource-section-size")
                {
                    using PEReader pe = new(new MemoryStream(image));
                    BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(pe.PEHeaders.CorHeaderStartOffset + 28, 4), 1);
                }
                Add("lib/net10.0/BGCS.CSharp.dll", image);
                if (mutation == "unrelated-owner-inventory") Add("lib/net10.0/BGCS.Alternate.dll", image);
            }
            if (mutation != "missing-inventory") Add("licenses/resources/english-words/embedded-resource-provenance.json", Encoding.UTF8.GetBytes(proof.ToJsonString()));
            if (mutation != "missing-license") Add("licenses/resources/english-words/LICENSE.md", mutation == "wrong-license" ? Encoding.UTF8.GetBytes("invented license") : File.ReadAllBytes(Path.Combine(notices, "LICENSE.md")));
        }

        internal void WritePackage(string? mutation = null)
        {
            string alphaNuspec = "<package><metadata><id>Alpha</id><version>1.0.0</version><license type=\"expression\">MIT</license></metadata></package>";
            string betaNuspec = "<package><metadata><id>Beta</id><version>2.0.0</version><license type=\"expression\">Apache-2.0</license></metadata></package>";
            string alphaSha = Convert.ToBase64String(AlphaHash);
            string betaSha = Convert.ToBase64String(Enumerable.Repeat((byte)0x42, 64).ToArray());
            Dictionary<string, object> libraries = new()
            {
                ["Application/3.0.0"] = new { type = "project", sha512 = "" },
                ["Alpha/1.0.0"] = new { type = "package", sha512 = "sha512-" + (mutation == "wrong-checksum" ? betaSha : alphaSha) },
                ["Beta/2.0.0"] = new { type = "package", sha512 = "sha512-" + betaSha }
            };
            Dictionary<string, object> target = new()
            {
                ["Application/3.0.0"] = new { dependencies = new Dictionary<string, string> { ["Alpha"] = "1.0.0" }, runtime = new Dictionary<string, object> { ["Application.dll"] = new { } } },
                ["Alpha/1.0.0"] = new { dependencies = new Dictionary<string, string> { ["Beta"] = mutation == "missing-edge" ? "9.0.0" : "2.0.0" }, runtime = new Dictionary<string, object> { ["lib/net10.0/Alpha.dll"] = new { } } },
                ["Beta/2.0.0"] = new { runtime = new Dictionary<string, object> { ["lib/net10.0/Beta.dll"] = new { } } }
            };
            object Inventory(string id, string version, string license, string sha, string nuspec, string[] dependencies) => new
            {
                id, version, license, licenseType = "expression", packageSha512 = sha,
                resolvedDependencies = dependencies, copyright = (string?)null,
                nuspec = new { path = id + "/" + version + "/" + id + ".nuspec", sha256 = Hash(nuspec) },
                notices = new[] { new { path = id + "/" + version + "/LICENSE.txt", sha256 = Hash(id + " license") } }
            };
            List<object> inventory = [Inventory("Alpha", "1.0.0", mutation == "wrong-license" ? "GPL-3.0-only" : "MIT", alphaSha, alphaNuspec, ["Beta/2.0.0"])];
            if (mutation != "missing-license")
                inventory.Add(Inventory("Beta", "2.0.0", "Apache-2.0", betaSha, betaNuspec, []));
            using ZipArchive archive = ZipFile.Open(Package, ZipArchiveMode.Create);
            void Add(string path, string text)
            {
                using StreamWriter writer = new(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
            Add("BGCS.nuspec", "<package><metadata><id>BGCS</id><version>3.0.0</version><license type=\"expression\">MIT</license><dependencies><group targetFramework=\"net10.0\"><dependency id=\"" + (mutation == "unresolved-product" ? "Absent" : "Alpha") + "\" version=\"[1.0.0]\" /></group></dependencies></metadata></package>");
            Add("tools/Application.dll", "application executable bytes");
            if (mutation != "missing-assembly")
                Add("tools/Alpha.dll", "alpha executable bytes");
            if (mutation == "duplicate")
                Add("tools/Alpha.dll", "ambiguous alternative bytes");
            Add("tools/Beta.dll", "beta executable bytes");
            Add("tools/Application.deps.json", JsonSerializer.Serialize(new { runtimeTarget = new { name = "net10.0" }, targets = new Dictionary<string, object> { ["net10.0"] = target }, libraries }));
            Add("licenses/dependencies/license-inventory.json", JsonSerializer.Serialize(inventory));
            Add("licenses/dependencies/Alpha/1.0.0/Alpha.nuspec", alphaNuspec);
            Add("licenses/dependencies/Alpha/1.0.0/LICENSE.txt", mutation == "changed-notice" ? "different license" : "Alpha license");
            Add("licenses/dependencies/Beta/2.0.0/Beta.nuspec", betaNuspec);
            Add("licenses/dependencies/Beta/2.0.0/LICENSE.txt", "Beta license");
        }

        internal void WriteNativePackage(string? mutation = null)
        {
            object Component(string id, string filename, string bytes, string license, string[] dependencies, object source) => new
            {
                id, version = "20.1.8", source,
                files = new[] { new { path = filename, sha256 = Hash(mutation == "wrong-native-bytes" && id == "LLVM" ? "not delivered" : bytes) } },
                licenses = new[] { new { path = "licenses/" + id + ".txt", sha256 = Hash(mutation == "wrong-native-license" && id == "LLVM" ? "fabricated license" : license) } },
                dependencies
            };
            List<object> components = [Component("ClangSharp", "libClangSharp.dylib", "native ClangSharp bytes", "actual ClangSharp license text",
                mutation == "missing-native-owner" ? [] : [mutation == "missing-native-edge" ? "Absent" : "LLVM"],
                new { uri = "git+https://github.com/dotnet/ClangSharp.git", gitCommit = new string('c', 40) })];
            if (mutation != "missing-native-owner")
                components.Add(Component("LLVM", "libclang.dylib", "native LLVM bytes", "actual LLVM license text", [],
                    new { uri = "https://github.com/llvm/llvm-project/archive/20.1.8.tar.gz", sha256 = new string('d', 64) }));
            using ZipArchive archive = ZipFile.Open(Package, ZipArchiveMode.Create);
            void Add(string path, string text)
            {
                using StreamWriter writer = new(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
            Add("Parser.nuspec", "<package><metadata><id>BGCS.ParserRuntime.osx-x64</id><version>3.0.0</version><license type=\"expression\">Apache-2.0 WITH LLVM-exception</license></metadata></package>");
            Add("runtimes/osx-x64/native/libclang.dylib", "native LLVM bytes");
            Add("runtimes/osx-x64/native/libClangSharp.dylib", "native ClangSharp bytes");
            Add("licenses/LLVM.txt", "actual LLVM license text");
            Add("licenses/ClangSharp.txt", "actual ClangSharp license text");
            Add("runtime-provenance.json", JsonSerializer.Serialize(new { schemaVersion = 1, components }));
        }

        internal int Run(string? inventoryMutation = null)
        {
            if (inventoryMutation != "missing")
            {
                List<object> packages = [];
                if (inventoryMutation != "missing-archive")
                    packages.Add(new { file = Path.GetFileName(Package), bytes = new FileInfo(Package).Length,
                        sha256 = inventoryMutation == "wrong-hash" ? new string('0', 64) : FileHash(Package) });
                if (inventoryMutation == "extra-archive")
                    packages.Add(new { file = "Absent.3.0.0.nupkg", bytes = 1L, sha256 = new string('0', 64) });
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(Package)!, "package-verification.json"), JsonSerializer.Serialize(new
                {
                    schemaVersion = 1, completeProductSet = true, sourceRevision = new string('a', 40),
                    checkedOutSourceSha256 = new string('b', 64), sourceRevisionStable = true,
                    sourceWorktreeDirty = inventoryMutation == "dirty-source", sourceBytesStable = inventoryMutation != "unstable-source",
                    sourceAtCompletion = new { sourceRevision = new string('a', 40), checkedOutSourceSha256 = new string('b', 64), sourceWorktreeDirty = false },
                    packages
                }));
            }
            return CliInvocation.Run(["supply-chain", Path.GetDirectoryName(Package)!, "--output", Output,
                "--revision", new string(inventoryMutation == "wrong-revision" ? 'c' : 'a', 40), "--timestamp", "2026-01-02T03:04:05Z"], m_root, TextWriter.Null, Error);
        }
        internal JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(Output, name)));
        internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        internal static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        public void Dispose() { Error.Dispose(); Directory.Delete(m_root, true); }
    }
}
