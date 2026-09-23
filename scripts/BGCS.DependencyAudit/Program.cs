using System.Text.Json;
using System.Xml.Linq;

if (args.Length == 2 && args[0] == "vulnerabilities")
    return CheckVulnerabilities(args[1]);
if (args.Length == 3 && args[0] == "licenses")
    return CheckLicenses(args[1], args[2]);

Console.Error.WriteLine("Usage: BGCS.DependencyAudit vulnerabilities <dotnet-list.json> | licenses <repository-root> <inventory.json>");
return 2;

static int CheckVulnerabilities(string reportPath)
{
    using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(reportPath)));
    List<string> vulnerabilities = [];
    Visit(report.RootElement, vulnerabilities);
    if (vulnerabilities.Count == 0)
    {
        Console.WriteLine("[dependency-audit] No known NuGet vulnerabilities were reported.");
        return 0;
    }

    foreach (string vulnerability in vulnerabilities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        Console.Error.WriteLine("[dependency-audit] vulnerable: " + vulnerability);
    return 1;
}

static void Visit(JsonElement value, List<string> vulnerabilities)
{
    if (value.ValueKind == JsonValueKind.Object)
    {
        if (value.TryGetProperty("vulnerabilities", out JsonElement entries) &&
            entries.ValueKind == JsonValueKind.Array && entries.GetArrayLength() > 0)
        {
            string package = value.TryGetProperty("id", out JsonElement id) ? id.GetString() ?? "unknown" : "unknown";
            string version = value.TryGetProperty("resolvedVersion", out JsonElement resolved)
                ? resolved.GetString() ?? "unknown"
                : "unknown";
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                string severity = entry.TryGetProperty("severity", out JsonElement severityValue)
                    ? severityValue.GetString() ?? "unknown"
                    : "unknown";
                string advisory = entry.TryGetProperty("advisoryurl", out JsonElement advisoryValue)
                    ? advisoryValue.GetString() ?? "unknown"
                    : entry.TryGetProperty("advisoryUrl", out advisoryValue)
                        ? advisoryValue.GetString() ?? "unknown"
                        : "unknown";
                vulnerabilities.Add($"{package} {version}: {severity} {advisory}");
            }
        }
        foreach (JsonProperty property in value.EnumerateObject())
            Visit(property.Value, vulnerabilities);
    }
    else if (value.ValueKind == JsonValueKind.Array)
    {
        foreach (JsonElement item in value.EnumerateArray())
            Visit(item, vulnerabilities);
    }
}

static int CheckLicenses(string repositoryRoot, string inventoryPath)
{
    string root = Path.GetFullPath(repositoryRoot);
    string packagesRoot = ResolvePackagesRoot();
    SortedSet<(string Id, string Version)> packages = new(Comparer<(string Id, string Version)>.Create((left, right) =>
    {
        int id = StringComparer.OrdinalIgnoreCase.Compare(left.Id, right.Id);
        return id != 0 ? id : StringComparer.OrdinalIgnoreCase.Compare(left.Version, right.Version);
    }));
    foreach (string assetsPath in Directory.GetFiles(root, "project.assets.json", SearchOption.AllDirectories)
                 .Where(path => !path.Contains(Path.DirectorySeparatorChar + "artifacts" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
    {
        using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        if (!assets.RootElement.TryGetProperty("libraries", out JsonElement libraries))
            continue;
        foreach (JsonProperty library in libraries.EnumerateObject())
        {
            if (!library.Value.TryGetProperty("type", out JsonElement type) || type.GetString() != "package")
                continue;
            int separator = library.Name.LastIndexOf('/');
            if (separator <= 0 || separator == library.Name.Length - 1)
                throw new InvalidDataException($"Invalid package identity '{library.Name}' in {assetsPath}.");
            packages.Add((library.Name[..separator], library.Name[(separator + 1)..]));
        }
    }
    if (packages.Count == 0)
        throw new InvalidDataException("No restored NuGet packages were found. Restore the solution before running the license gate.");

    List<LicenseEntry> inventory = [];
    List<string> failures = [];
    foreach ((string id, string version) in packages)
    {
        string packageDirectory = Path.Combine(packagesRoot, id.ToLowerInvariant(), version.ToLowerInvariant());
        string nuspec = Directory.Exists(packageDirectory)
            ? Directory.GetFiles(packageDirectory, "*.nuspec", SearchOption.TopDirectoryOnly).SingleOrDefault() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrEmpty(nuspec))
        {
            failures.Add($"{id} {version}: restored .nuspec not found under {packageDirectory}");
            continue;
        }
        XDocument document = XDocument.Load(nuspec, LoadOptions.None);
        XElement? metadata = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "metadata");
        XElement? license = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName == "license");
        XElement? licenseUrl = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName == "licenseUrl");
        string declaration = license == null
            ? licenseUrl?.Value.Trim() ?? string.Empty
            : string.Equals(license.Attribute("type")?.Value, "expression", StringComparison.OrdinalIgnoreCase)
                ? license.Value.Trim()
                : "file:" + license.Value.Trim();
        if (string.IsNullOrWhiteSpace(declaration))
            failures.Add($"{id} {version}: no license declaration");
        if (IsDenied(declaration))
            failures.Add($"{id} {version}: denied license '{declaration}'");
        inventory.Add(new(id, version, declaration));
    }

    string output = Path.GetFullPath(inventoryPath);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, JsonSerializer.Serialize(new
    {
        policyVersion = 1,
        deniedLicenseFamilies = new[] { "AGPL", "GPL", "SSPL" },
        packages = inventory
    }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    foreach (string failure in failures)
        Console.Error.WriteLine("[dependency-audit] " + failure);
    if (failures.Count > 0)
        return 1;
    Console.WriteLine($"[dependency-audit] {inventory.Count} package license declarations passed; inventory: {output}");
    return 0;
}

static string ResolvePackagesRoot()
{
    string? configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
    string root = string.IsNullOrWhiteSpace(configured)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
        : configured;
    return Path.GetFullPath(root);
}

static bool IsDenied(string declaration)
{
    string value = declaration.ToUpperInvariant();
    string withoutLesserGpl = value.Replace("LGPL", string.Empty, StringComparison.Ordinal);
    return value.Contains("AGPL", StringComparison.Ordinal) ||
        value.Contains("SSPL", StringComparison.Ordinal) ||
        withoutLesserGpl.Contains("GPL", StringComparison.Ordinal);
}

internal sealed record LicenseEntry(string Package, string Version, string License);
