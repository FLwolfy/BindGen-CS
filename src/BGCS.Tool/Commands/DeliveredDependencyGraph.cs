using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace BGCS.Tool.Commands;

/// <summary>Reads delivered archives without consulting the machine's restore cache.</summary>
internal sealed class DeliveredDependencyGraph
{
    private readonly SortedDictionary<string, Package> m_packages = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, FileEntry> m_files = new(StringComparer.Ordinal);
    private readonly SortedSet<(string source, string kind, string target)> m_relationships = new(
        Comparer<(string source, string kind, string target)>.Create((
            left,
            right
        ) => {
            int source = StringComparer.Ordinal.Compare(left.source, right.source);
            if (source != 0) return source;
            int kind = StringComparer.Ordinal.Compare(left.kind, right.kind);
            return kind != 0 ? kind : StringComparer.Ordinal.Compare(left.target, right.target);
        }));
    private readonly Dictionary<string, Dependency> m_dependencies = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string source, string dependency)> m_pending = [];
    private readonly Dictionary<string, string> m_products = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string source, string id, string version)> m_productDependencies = [];
    private readonly SortedDictionary<string, string> m_extractedLicenses = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, object> m_nativeMaterials = new(StringComparer.Ordinal);

    internal void AddArtifact(
        string path,
        string name,
        string sha256
    ) {
        string id = Id("Artifact", name);
        Package package = new(id, name, null, "NOASSERTION", "NOASSERTION", sha256, null, "NOASSERTION");
        m_packages.Add(id, package);
        m_relationships.Add(("SPDXRef-DOCUMENT", "DESCRIBES", id));
        if (!path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            return;

        using ZipArchive archive = ZipFile.OpenRead(path);
        Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
        Dictionary<string, string> hashes = new(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries.Where(item => !item.FullName.EndsWith('/')).OrderBy(item => item.FullName, StringComparer.Ordinal))
        {
            ValidatePath(entry.FullName);
            if (!entries.TryAdd(entry.FullName, entry))
                throw new InvalidDataException($"The delivered archive contains duplicate entries: {name}/{entry.FullName}");
            if (entry.Length > 512L * 1024 * 1024)
                throw new InvalidDataException($"The delivered archive entry exceeds its hashing limit: {name}/{entry.FullName}");
            using Stream stream = entry.Open();
            string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            hashes.Add(entry.FullName, hash);
            string fileId = Id("File", name + "/" + entry.FullName);
            m_files.Add(fileId, new(fileId, name + "/" + entry.FullName, hash));
            m_relationships.Add((id, "CONTAINS", fileId));
        }
        ZipArchiveEntry[] nuspecs = entries.Values.Where(entry => !entry.FullName.Contains('/') && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (nuspecs.Length != 1)
            throw new InvalidDataException($"The delivered package requires exactly one root nuspec: {name}");
        XElement metadata = Metadata(nuspecs[0]);
        string productName = RequiredElement(metadata, "id");
        string version = RequiredElement(metadata, "version");
        string license = ReadLicense(metadata, entries, "");
        m_packages[id] = package with { name = productName, versionInfo = version, licenseDeclared = license };
        if (path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            string identity = productName + "/" + version;
            if (!m_products.TryAdd(identity, id))
                throw new InvalidDataException($"Multiple delivered packages claim the same NuGet identity: {identity}");
            foreach (XElement dependency in metadata.Descendants().Where(element => element.Name.LocalName == "dependency"))
                m_productDependencies.Add((id, RequiredAttribute(dependency, "id"), RequiredAttribute(dependency, "version")));
        }
        foreach (ZipArchiveEntry inventory in entries.Values.Where(entry => entry.FullName.EndsWith("licenses/dependencies/license-inventory.json", StringComparison.Ordinal)))
            AddInventory(id, name, inventory, entries, hashes);
        foreach (ZipArchiveEntry deps in entries.Values.Where(entry => entry.FullName.EndsWith(".deps.json", StringComparison.Ordinal)))
            AddExecutionGraph(id, name, deps, entries, hashes);
        foreach (ZipArchiveEntry provenance in entries.Values.Where(entry => entry.FullName.EndsWith("runtime-provenance.json", StringComparison.Ordinal)))
            AddNativeProvenance(id, name, provenance, entries, hashes);
        ZipArchiveEntry[] resourceProofs = entries.Values.Where(entry => entry.FullName.EndsWith("embedded-resource-provenance.json", StringComparison.Ordinal)).ToArray();
        HashSet<(string assembly, string resource)> provenResources = [];
        foreach (ZipArchiveEntry provenance in resourceProofs)
            provenResources.Add(AddEmbeddedResourceProvenance(id, name, provenance, entries, hashes));
        if (!provenResources.Contains(("BGCS.CSharp.dll", "BGCS.CSharp.Naming.en_EN.txt"))
            && entries.Values.Where(entry => entry.Name == "BGCS.CSharp.dll")
            .Any(entry => EmbeddedResourceProof.Read(entry, "BGCS.CSharp.Naming.en_EN.txt") is not null))
            throw new InvalidDataException("The delivered English vocabulary has no embedded-resource provenance/license inventory.");
    }

    private (string assembly, string resource) AddEmbeddedResourceProvenance(
        string artifactId,
        string artifactName,
        ZipArchiveEntry provenance,
        Dictionary<string, ZipArchiveEntry> entries,
        Dictionary<string, string> hashes
    ) {
        using JsonDocument document = ReadJson(provenance);
        JsonElement value = document.RootElement;
        if (value.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("The embedded resource provenance schema is unsupported.");
        string name = Required(value, "id"), revision = Hex(Required(value, "revision"), 40);
        string assembly = Required(value, "assembly"), resource = Required(value, "resource");
        ValidatePath(assembly);
        if (assembly.Contains('/')) throw new InvalidDataException("An embedded resource requires an exact assembly filename.");
        string expected = Hex(Required(value, "sha256"), 64);
        int length = value.GetProperty("bytes").GetInt32();
        string sourceHash = Hex(Required(value, "sourceSha256"), 64);
        int sourceLength = value.GetProperty("sourceBytes").GetInt32();
        string sourceUri = Required(value, "sourceUri");
        if (!Uri.TryCreate(sourceUri, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https" || !uri.AbsolutePath.Contains("/" + revision + "/", StringComparison.Ordinal))
            throw new InvalidDataException("The embedded resource upstream must have an exact pinned HTTPS source revision.");
        JsonElement license = value.GetProperty("license");
        string licenseRelative = Required(license, "path");
        ValidatePath(licenseRelative);
        string root = provenance.FullName[..^"embedded-resource-provenance.json".Length];
        string licensePath = root + licenseRelative;
        VerifyEntry(licensePath, Hex(Required(license, "sha256"), 64), entries, hashes);
        if (Required(license, "expression") != "Unlicense" || !ReadText(entries[licensePath]).StartsWith("This is free and unencumbered software released into the public domain.", StringComparison.Ordinal))
            throw new InvalidDataException("The embedded vocabulary must retain its audited original Unlicense dedication.");
        ZipArchiveEntry[] owners = entries.Values.Where(entry => entry.Name == assembly).ToArray();
        if (owners.Length == 0) throw new InvalidDataException("The embedded resource has no delivered owner assembly.");
        string node = Id("EmbeddedResource", name + "/" + revision);
        Package component = new(node, name, revision, sourceUri, "Unlicense", expected, null, "NOASSERTION");
        if (m_packages.TryGetValue(node, out Package? previous) && previous != component)
            throw new InvalidDataException("The same embedded resource identity has contradictory delivered provenance.");
        m_packages[node] = component;
        m_relationships.Add((artifactId, "DEPENDS_ON", node));
        m_relationships.Add((node, "CONTAINS", Id("File", artifactName + "/" + licensePath)));
        foreach (ZipArchiveEntry owner in owners)
        {
            byte[] data = EmbeddedResourceProof.Read(owner, resource) ?? throw new InvalidDataException("A declared resource is absent from its delivered assembly.");
            if (data.Length != length || EmbeddedResourceProof.Digest(data) != expected)
                throw new InvalidDataException("The embedded resource bytes contradict their actual PE payload digest.");
            byte[] original = Required(value, "transformation") switch
            {
                "crlf-to-lf" when !data.Contains((byte)'\r') => System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(data).Replace("\n", "\r\n", StringComparison.Ordinal)),
                _ => throw new InvalidDataException("The embedded resource has an unsupported or noncanonical transformation.")
            };
            if (original.Length != sourceLength || EmbeddedResourceProof.Digest(original) != sourceHash)
                throw new InvalidDataException("The embedded resource cannot reproduce its exact pinned upstream bytes.");
            string fileId = Id("File", artifactName + "/" + owner.FullName + "!resource/" + resource);
            m_files.TryAdd(fileId, new(fileId, artifactName + "/" + owner.FullName + "!resource/" + resource, expected));
            m_relationships.Add((Id("File", artifactName + "/" + owner.FullName), "CONTAINS", fileId));
            m_relationships.Add((node, "CONTAINS", fileId));
        }
        m_nativeMaterials.TryAdd(name + "/" + revision, new { uri = sourceUri, digest = new { sha256 = sourceHash, gitCommit = revision } });
        return (assembly, resource);
    }

    internal void Complete()
    {
        foreach ((string source, string dependency) in m_pending)
        {
            if (!m_dependencies.TryGetValue(dependency, out Dependency? resolved))
                throw new InvalidDataException($"The delivered dependency graph is incomplete: {source} -> {dependency}");
            m_relationships.Add((source, "DEPENDS_ON", resolved.id));
        }
        foreach ((string source, string name, string constraint) in m_productDependencies)
        {
            // Release packages use exact pins. A range cannot identify the bytes whose license was audited.
            string version = constraint.StartsWith('[') && constraint.EndsWith(']') && !constraint.Contains(',')
                ? constraint[1..^1] : constraint;
            string identity = name + "/" + version;
            if (m_products.TryGetValue(identity, out string? product))
                m_relationships.Add((source, "DEPENDS_ON", product));
            else if (m_dependencies.TryGetValue(identity, out Dependency? dependency))
                m_relationships.Add((source, "DEPENDS_ON", dependency.id));
            else
                throw new InvalidDataException($"The delivered package dependency has no exact accepted graph/license evidence: {identity}");
        }
    }

    internal object[] Packages => m_packages.Values.Select(package => (object)new
    {
        SPDXID = package.id,
        package.name,
        package.versionInfo,
        package.downloadLocation,
        filesAnalyzed = false,
        checksums = package.sha256 is not null ? new[] { new { algorithm = "SHA256", checksumValue = package.sha256 } }
            : package.sha512 is not null ? new[] { new { algorithm = "SHA512", checksumValue = package.sha512 } }
            : [],
        licenseConcluded = "NOASSERTION",
        package.licenseDeclared,
        package.copyrightText
    }).ToArray();

    internal object[] Files => m_files.Values.Select(file => (object)new
    {
        SPDXID = file.id,
        fileName = file.path,
        checksums = new[] { new { algorithm = "SHA256", checksumValue = file.sha256 } },
        licenseConcluded = "NOASSERTION",
        copyrightText = "NOASSERTION"
    }).ToArray();

    internal object[] Relationships => m_relationships.Select(edge => (object)new
    {
        spdxElementId = edge.source,
        relationshipType = edge.kind,
        relatedSpdxElement = edge.target
    }).ToArray();

    internal object[] Materials => m_dependencies.Values.OrderBy(item => item.identity, StringComparer.Ordinal).Select(dependency => (object)new
    {
        uri = "pkg:nuget/" + dependency.identity.Replace('/', '@'),
        digest = new { sha512 = dependency.sha512 }
    }).Concat(m_nativeMaterials.Values).ToArray();

    internal object[] ExtractedLicenses => m_extractedLicenses.Select(pair => (object)new
    {
        licenseId = pair.Key,
        extractedText = pair.Value
    }).ToArray();

    private void AddInventory(
        string artifactId,
        string artifactName,
        ZipArchiveEntry inventory,
        Dictionary<string, ZipArchiveEntry> entries,
        Dictionary<string, string> hashes
    ) {
        string root = inventory.FullName[..^"license-inventory.json".Length];
        using JsonDocument document = ReadJson(inventory);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The delivered license inventory must be an array.");
        HashSet<string> identities = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            string identity = Required(item, "id") + "/" + Required(item, "version");
            if (!identities.Add(identity))
                throw new InvalidDataException($"The license inventory repeats a dependency: {identity}");
            string licenseText = Required(item, "license");
            string licenseType = Required(item, "licenseType");
            if (licenseType is not ("expression" or "file" or "url"))
                throw new InvalidDataException("A dependency license declaration has an unsupported kind.");
            string sha512 = Sha512(Required(item, "packageSha512"));
            JsonElement nuspec = item.GetProperty("nuspec");
            string nuspecPath = root + Required(nuspec, "path");
            VerifyEntry(nuspecPath, Required(nuspec, "sha256"), entries, hashes);
            XElement metadata = Metadata(entries[nuspecPath]);
            if (!string.Equals(RequiredElement(metadata, "id") + "/" + RequiredElement(metadata, "version"), identity, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The delivered license metadata contradicts its NuGet identity: {identity}");
            XElement? declared = metadata.Elements().SingleOrDefault(element => element.Name.LocalName == "license");
            string actualLicense = declared?.Value ?? metadata.Elements().SingleOrDefault(element => element.Name.LocalName == "licenseUrl")?.Value ?? "";
            string actualType = declared?.Attribute("type")?.Value ?? "url";
            if (licenseText != actualLicense || licenseType != actualType)
                throw new InvalidDataException($"The delivered license inventory contradicts its nuspec: {identity}");
            foreach (JsonElement notice in item.GetProperty("notices").EnumerateArray())
                VerifyEntry(root + Required(notice, "path"), Required(notice, "sha256"), entries, hashes);
            string license = licenseType == "expression" ? licenseText
                : licenseType == "file" ? "LicenseRef-" + Hash(identity + "/" + licenseText) : "NOASSERTION";
            if (licenseType == "file")
            {
                VerifyEntry(root + identity + "/" + licenseText, hashes.GetValueOrDefault(root + identity + "/" + licenseText) ?? "missing", entries, hashes);
                AddExtractedLicense(license, entries[root + identity + "/" + licenseText]);
            }
            string[] dependencies = item.GetProperty("resolvedDependencies").EnumerateArray().Select(element => element.GetString()
                ?? throw new InvalidDataException("A resolved dependency identity is null.")).Order(StringComparer.Ordinal).ToArray();
            Dependency candidate = new(Id("NuGet", identity.ToLowerInvariant()), identity, sha512, license,
                item.TryGetProperty("copyright", out JsonElement copyright) && copyright.ValueKind == JsonValueKind.String ? copyright.GetString()! : "NOASSERTION", dependencies);
            if (m_dependencies.TryGetValue(identity, out Dependency? previous))
            {
                if (previous.sha512 != candidate.sha512 || previous.license != candidate.license || previous.copyright != candidate.copyright)
                    throw new InvalidDataException($"Delivered product inventories contradict the same dependency identity: {identity}");
            }
            else
            {
                m_dependencies.Add(identity, candidate);
                m_packages.Add(candidate.id, new(candidate.id, identity[..identity.LastIndexOf('/')], identity[(identity.LastIndexOf('/') + 1)..],
                    "https://www.nuget.org/packages/" + identity, license, null, sha512, candidate.copyright));
            }
            m_relationships.Add((artifactId, "DEPENDS_ON", candidate.id));
            foreach (string dependency in dependencies)
                m_pending.Add((candidate.id, dependency));
            m_relationships.Add((candidate.id, "DESCRIBED_BY", Id("File", artifactName + "/" + nuspecPath)));
        }
    }

    private void AddExecutionGraph(
        string artifactId,
        string artifactName,
        ZipArchiveEntry deps,
        Dictionary<string, ZipArchiveEntry> entries,
        Dictionary<string, string> hashes
    ) {
        using JsonDocument document = ReadJson(deps);
        JsonElement libraries = document.RootElement.GetProperty("libraries");
        string targetName = Required(document.RootElement.GetProperty("runtimeTarget"), "name");
        JsonElement target = document.RootElement.GetProperty("targets").GetProperty(targetName);
        Dictionary<string, string> nodes = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty library in target.EnumerateObject())
        {
            JsonElement details = libraries.GetProperty(library.Name);
            string type = Required(details, "type");
            if (type == "package")
            {
                if (!m_dependencies.TryGetValue(library.Name, out Dependency? dependency)
                    || dependency.sha512 != Sha512(Required(details, "sha512")))
                    throw new InvalidDataException($"Execution dependency lacks matching delivered license/checksum evidence: {library.Name}");
                nodes.Add(library.Name, dependency.id);
            }
            else if (type == "project")
            {
                string node = Id("Module", library.Name);
                int slash = library.Name.LastIndexOf('/');
                m_packages.TryAdd(node, new(node, library.Name[..slash], library.Name[(slash + 1)..], "NOASSERTION", "NOASSERTION", null, null, "NOASSERTION"));
                nodes.Add(library.Name, node);
            }
            else
                throw new InvalidDataException($"Execution dependency has an unaudited library kind: {library.Name} ({type})");
            m_relationships.Add((artifactId, "DEPENDS_ON", nodes[library.Name]));
        }
        string directory = deps.FullName[..(deps.FullName.LastIndexOf('/') + 1)];
        foreach (JsonProperty library in target.EnumerateObject())
        {
            string node = nodes[library.Name];
            if (library.Value.TryGetProperty("dependencies", out JsonElement dependencies))
                foreach (JsonProperty dependency in dependencies.EnumerateObject())
                {
                    string identity = dependency.Name + "/" + dependency.Value.GetString();
                    if (!nodes.TryGetValue(identity, out string? related))
                        throw new InvalidDataException($"The actual execution dependency graph is incomplete: {library.Name} -> {identity}");
                    m_relationships.Add((node, "DEPENDS_ON", related));
                }
            foreach (string category in new[] { "runtime", "resources", "native", "runtimeTargets" })
            {
                if (!library.Value.TryGetProperty(category, out JsonElement assets))
                    continue;
                foreach (JsonProperty asset in assets.EnumerateObject())
                {
                    string filename = asset.Name[(asset.Name.LastIndexOf('/') + 1)..];
                    if (filename == "_._")
                        continue;
                    string direct = directory + asset.Name;
                    string flattened = directory + filename;
                    string? actual = entries.ContainsKey(direct) ? direct : entries.ContainsKey(flattened) ? flattened : null;
                    if (actual is null && category == "resources")
                    {
                        string locale = Required(asset.Value, "locale");
                        string localized = directory + locale + "/" + filename;
                        actual = entries.ContainsKey(localized) ? localized : null;
                    }
                    // Runtime-target entries also describe other RIDs. Their package dependency
                    // remains explicit, but only bytes present in this archive are CONTAINS edges.
                    if (actual is null && category is ("runtime" or "resources"))
                        throw new InvalidDataException($"The delivered execution closure is missing its managed asset: {artifactName}/{deps.FullName}: {asset.Name}");
                    if (actual is not null)
                    {
                        _ = hashes[actual];
                        m_relationships.Add((node, "CONTAINS", Id("File", artifactName + "/" + actual)));
                    }
                }
            }
        }
    }

    private void AddNativeProvenance(
        string artifactId,
        string artifactName,
        ZipArchiveEntry provenance,
        Dictionary<string, ZipArchiveEntry> entries,
        Dictionary<string, string> hashes
    ) {
        // SDK/tool notices may include licenses for another host. Only an archive
        // actually delivering Intel dylibs supplies a native component relationship.
        if (!entries.Keys.Any(path => path.EndsWith(".dylib", StringComparison.Ordinal)))
            return;
        using JsonDocument document = ReadJson(provenance);
        JsonElement[] components = document.RootElement.GetProperty("components").EnumerateArray().ToArray();
        if (components.Length == 0)
            throw new InvalidDataException("Redistributed native code lacks its actual component provenance.");
        if (!components.SelectMany(component => component.GetProperty("files").EnumerateArray())
            .Any(file => entries.Keys.Any(path => path.EndsWith("/" + Required(file, "path"), StringComparison.Ordinal)
                && hashes[path] == Required(file, "sha256"))))
        {
            if (m_packages[artifactId].name == "BGCS.ParserRuntime.osx-x64")
                throw new InvalidDataException("The Intel technical package has no matching native component bytes.");
            return;
        }
        string root = provenance.FullName[..^"runtime-provenance.json".Length];
        Dictionary<string, string> nodes = new(StringComparer.Ordinal);
        HashSet<string> ownedFiles = new(StringComparer.Ordinal);
        foreach (JsonElement component in components)
        {
            string name = Required(component, "id");
            string version = Required(component, "version");
            JsonElement source = component.GetProperty("source");
            string uri = Required(source, "uri");
            Dictionary<string, string> sourceDigest = new(StringComparer.Ordinal);
            if (source.TryGetProperty("sha256", out JsonElement sha))
                sourceDigest.Add("sha256", Hex(sha.GetString(), 64));
            else if (source.TryGetProperty("gitCommit", out JsonElement commit))
                sourceDigest.Add("gitCommit", Hex(commit.GetString(), 40));
            else
                throw new InvalidDataException("A native component has no exact source identity.");
            string identity = name + "/" + version + "/" + uri + "/" + sourceDigest.Single().Value;
            string node = Id("Native", identity);
            if (!nodes.TryAdd(name, node))
                throw new InvalidDataException("Native provenance repeats a component identity.");
            StringBuilder licenseText = new();
            foreach (JsonElement license in component.GetProperty("licenses").EnumerateArray())
            {
                string path = root + Required(license, "path");
                VerifyEntry(path, Required(license, "sha256"), entries, hashes);
                licenseText.Append(ReadText(entries[path])).Append('\n');
            }
            if (licenseText.Length == 0)
                throw new InvalidDataException("A redistributed native component lacks actual license text.");
            string licenseId = "LicenseRef-" + Hash(licenseText.ToString());
            m_extractedLicenses[licenseId] = licenseText.ToString();
            m_packages.TryAdd(node, new(node, name, version, uri, licenseId, null, null, "NOASSERTION"));
            m_nativeMaterials.TryAdd(identity, new { uri, digest = sourceDigest });
            m_relationships.Add((artifactId, "DEPENDS_ON", node));
            foreach (JsonElement file in component.GetProperty("files").EnumerateArray())
            {
                string relative = Required(file, "path");
                ValidatePath(relative);
                if (relative.Contains('/'))
                    throw new InvalidDataException("Native component file identities must be relocated dylib names.");
                string expected = Hex(Required(file, "sha256"), 64);
                string[] actual = entries.Keys.Where(path => path.EndsWith("/" + relative, StringComparison.Ordinal) && hashes[path] == expected).ToArray();
                if (actual.Length == 0)
                    throw new InvalidDataException("A native component has no matching delivered binary bytes: " + relative);
                foreach (string path in actual)
                {
                    if (!ownedFiles.Add(path))
                        throw new InvalidDataException("Multiple native components claim the same delivered binary.");
                    m_relationships.Add((node, "CONTAINS", Id("File", artifactName + "/" + path)));
                }
            }
        }
        foreach (JsonElement component in components)
            foreach (JsonElement dependency in component.GetProperty("dependencies").EnumerateArray())
            {
                if (!nodes.TryGetValue(dependency.GetString() ?? "", out string? target))
                    throw new InvalidDataException("The actual native dependency graph is incomplete.");
                m_relationships.Add((nodes[Required(component, "id")], "DEPENDS_ON", target));
            }
        if (entries.Keys.Where(path => path.EndsWith(".dylib", StringComparison.Ordinal)).Any(path => !ownedFiles.Contains(path)))
            throw new InvalidDataException("A delivered native binary has no component/license provenance.");
    }

    private string ReadLicense(
        XElement metadata,
        Dictionary<string, ZipArchiveEntry> entries,
        string root
    ) {
        XElement? license = metadata.Elements().SingleOrDefault(element => element.Name.LocalName == "license");
        if (license is null)
            throw new InvalidDataException("A delivered NuGet product lacks a license declaration.");
        if (license.Attribute("type")?.Value == "expression" && !string.IsNullOrWhiteSpace(license.Value))
            return license.Value;
        if (license.Attribute("type")?.Value == "file" && entries.ContainsKey(root + license.Value))
        {
            string id = "LicenseRef-" + Hash(ReadText(entries[root + license.Value]));
            AddExtractedLicense(id, entries[root + license.Value]);
            return id;
        }
        throw new InvalidDataException("The delivered NuGet product has an invalid license declaration.");
    }

    private static XElement Metadata(ZipArchiveEntry entry)
    {
        if (entry.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("A delivered nuspec exceeds its input limit.");
        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);
        return document.Root?.Elements().SingleOrDefault(element => element.Name.LocalName == "metadata")
            ?? throw new InvalidDataException("A delivered nuspec has no unique metadata element.");
    }

    private void AddExtractedLicense(
        string id,
        ZipArchiveEntry entry
    ) {
        string text = ReadText(entry);
        if (m_extractedLicenses.TryGetValue(id, out string? previous) && previous != text)
            throw new InvalidDataException("Delivered license identifiers refer to different text.");
        m_extractedLicenses[id] = text;
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        if (entry.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("A delivered license exceeds its input limit.");
        using Stream stream = entry.Open();
        using StreamReader reader = new(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static JsonDocument ReadJson(ZipArchiveEntry entry)
    {
        if (entry.Length > 16 * 1024 * 1024)
            throw new InvalidDataException("Delivered dependency metadata exceeds its input limit.");
        using Stream stream = entry.Open();
        return JsonDocument.Parse(stream);
    }

    private static void VerifyEntry(
        string path,
        string hash,
        Dictionary<string, ZipArchiveEntry> entries,
        Dictionary<string, string> hashes
    ) {
        ValidatePath(path);
        if (!entries.ContainsKey(path) || !hashes.TryGetValue(path, out string? actual) || actual != hash)
            throw new InvalidDataException($"Delivered dependency license/nuspec bytes differ from their audited hash: {path}");
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\') || path.Split('/').Any(part => part is ".." or "." or ""))
            throw new InvalidDataException($"A delivered archive entry has an unsafe path: {path}");
    }

    private static string Sha512(string text)
    {
        string value = text.StartsWith("sha512-", StringComparison.Ordinal) ? text[7..] : text;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(value); }
        catch (FormatException exception) { throw new InvalidDataException("A resolved NuGet package lacks a valid SHA-512 digest.", exception); }
        if (bytes.Length != 64)
            throw new InvalidDataException("A resolved NuGet package lacks a 512-bit checksum.");
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Hex(
        string? value,
        int length
    ) => value is not null && value.Length == length && value.All(char.IsAsciiHexDigit)
        ? value.ToLowerInvariant() : throw new InvalidDataException("A delivered native provenance digest is invalid.");

    private static string Required(
        JsonElement element,
        string name
    ) => element.GetProperty(name).ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(element.GetProperty(name).GetString()) ? element.GetProperty(name).GetString()!
        : throw new InvalidDataException($"Delivered dependency metadata is missing '{name}'.");
    private static string RequiredElement(
        XElement element,
        string name
    ) => element.Elements().SingleOrDefault(child => child.Name.LocalName == name)?.Value
        is { Length: > 0 } value ? value : throw new InvalidDataException($"A delivered nuspec is missing '{name}'.");
    private static string RequiredAttribute(
        XElement element,
        string name
    ) => element.Attribute(name)?.Value
        is { Length: > 0 } value ? value : throw new InvalidDataException($"A delivered nuspec dependency is missing '{name}'.");
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static string Id(
        string kind,
        string identity
    ) => "SPDXRef-" + kind + "-" + Hash(identity);
    private sealed record Package(
        string id,
        string name,
        string? versionInfo,
        string downloadLocation,
        string licenseDeclared,
        string? sha256,
        string? sha512,
        string copyrightText
    );
    private sealed record FileEntry(
        string id,
        string path,
        string sha256
    );
    private sealed record Dependency(
        string id,
        string identity,
        string sha512,
        string license,
        string copyright,
        string[] dependencies
    );
}
