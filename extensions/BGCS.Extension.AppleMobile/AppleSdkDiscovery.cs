using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using BGCS.Core.Targets;
using BGCS.Core.Inputs;


namespace BGCS.Extension.AppleMobile;

/// <summary>Checks the selected Apple SDK identity before resolving its parser header environment.</summary>
public static class AppleSdkDiscovery
{
    /// <summary>Validates SDK family and version independently for device and simulator targets.</summary>
    /// <param name="targetId">iOS device or iOS simulator target identity.</param>
    /// <param name="sdkRoot">The explicit installed SDK root.</param>
    /// <param name="compilerPath">An optional read-only compiler installation hint; no driver is executed.</param>
    /// <returns>An immutable target pinned to the selected SDK path/version.</returns>
    public static NativeTargetDescriptor Resolve(
        NativeTargetId targetId,
        string sdkRoot,
        string? compilerPath = null
    ) {
        string root = Path.GetFullPath(sdkRoot);
        NativeTargetDescriptor target = ResolveTarget(targetId, root);
        string family = target.platformId switch { "ios" => "iphoneos", "ios-simulator" => "iphonesimulator", _ => throw new NotSupportedException("The requested target is not an Apple SDK family.") };
        string version = ReadValidatedVersion(root, family);
        string? compiler = compilerPath;
        string includes = Path.Combine(root, "usr", "include"), cxx = Path.Combine(includes, "c++", "v1");
        if (!Directory.Exists(includes)) throw new IOException("The selected Apple SDK has no system include directory.");
        NativeToolchainDescriptor toolchain = new(compiler, root, [includes], cxxSystemIncludeFolders: Directory.Exists(cxx) ? [cxx] : [], sdkVersion: version);
        return new(target.targetId, target.platformId, target.architectureId, target.abiId, target.triple, toolchain, target.capabilities, target.byteOrder);
    }
    /// <summary>Finds existing Apple SDK metadata without executing SDK tools.</summary>
    /// <param name="sdkName">iphoneos or iphonesimulator.</param>
    /// <returns>The explicitly selected or installed SDK root, or null.</returns>
    /// <exception cref="InvalidDataException">An explicit SDKROOT does not identify the requested SDK family and version.</exception>
    /// <exception cref="DirectoryNotFoundException">An explicitly selected SDKROOT does not exist.</exception>
    public static string? FindInstalledRoot(string sdkName)
    {
        string family = sdkName switch { "iphoneos" => "iPhoneOS", "iphonesimulator" => "iPhoneSimulator", _ => throw new ArgumentException("Unknown Apple SDK family.", nameof(sdkName)) };
        string? configured = Environment.GetEnvironmentVariable("SDKROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string root = Path.GetFullPath(configured);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The explicitly selected SDKROOT does not exist: " + root);
            _ = ReadValidatedVersion(root, sdkName);
            return root;
        }
        List<string> roots = [];
        string? developer = Environment.GetEnvironmentVariable("DEVELOPER_DIR");
        if (!string.IsNullOrWhiteSpace(developer))
        {
            developer = Path.GetFullPath(developer);
            string bundleDeveloper = Path.Combine(developer, "Contents", "Developer");
            if (Directory.Exists(bundleDeveloper)) developer = bundleDeveloper;
            roots.Add(Path.Combine(developer, "Platforms", family + ".platform", "Developer", "SDKs"));
            roots.Add(Path.Combine(developer, "SDKs"));
        }
        roots.Add("/Library/Developer/CommandLineTools/SDKs");
        roots.Add("/Applications/Xcode.app/Contents/Developer/Platforms/" + family + ".platform/Developer/SDKs");
        foreach (string parent in roots.Where(Directory.Exists))
        {
            string? selected = Directory.GetDirectories(parent, family + "*.sdk").OrderDescending(StringComparer.Ordinal).FirstOrDefault();
            if (selected is not null) return selected;
        }
        return null;
    }

    private static string ReadValidatedVersion(
        string root,
        string family
    ) {
        Dictionary<string, string> settings = ReadSettings(root);
        if (!settings.TryGetValue("CanonicalName", out string? canonical) || !canonical.StartsWith(family, StringComparison.Ordinal)
            || !settings.TryGetValue("Version", out string? version) || !System.Version.TryParse(version, out _))
            throw new InvalidDataException("The selected Apple SDK does not identify the requested device/simulator family and exact version.");
        return version;
    }

    private static Dictionary<string, string> ReadSettings(string root)
    {
        string json = Path.Combine(root, "SDKSettings.json"), plist = Path.Combine(root, "SDKSettings.plist");
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        if (File.Exists(json))
        {
            using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(json), new() { MaxDepth = 64 });
            foreach (JsonProperty property in settings.RootElement.EnumerateObject())
                if (property.Value.ValueKind == JsonValueKind.String) values.Add(property.Name, property.Value.GetString()!);
            return values;
        }
        if (!File.Exists(plist)) throw new FileNotFoundException("The selected Apple SDK has no SDKSettings manifest.", plist);
        using XmlReader reader = XmlReader.Create(plist, new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        XDocument document = XDocument.Load(reader);
        XElement[] children = document.Root?.Element("dict")?.Elements().ToArray() ?? throw new InvalidDataException("The Apple SDK settings plist is not a dictionary.");
        for (int index = 0; index + 1 < children.Length; index++)
            if (children[index].Name.LocalName == "key" && children[index + 1].Name.LocalName == "string") values.Add(children[index].Value, children[++index].Value);
        return values;
    }
    private static NativeTargetDescriptor ResolveTarget(
        NativeTargetId targetId,
        string root
    ) {
        if (!new AppleMobileTargetProvider().TryResolve(new(targetId, new(sysRoot: root)), out NativeTargetDescriptor? target))
            throw new NotSupportedException("The requested target is not an Apple mobile target.");
        return target;
    }
}
