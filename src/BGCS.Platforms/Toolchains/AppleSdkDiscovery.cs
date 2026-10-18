using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using BGCS.Contracts.Targets;
using BGCS.Contracts.Inputs;
using BGCS.Platforms.Targeting;

namespace BGCS.Platforms.Toolchains;

/// <summary>Checks the selected Apple SDK identity before resolving its target headers and libraries.</summary>
public static class AppleSdkDiscovery
{
    /// <summary>Validates SDK family and version independently for macOS, device and simulator targets.</summary>
    /// <param name="targetId">macOS, iOS device or iOS simulator target identity.</param>
    /// <param name="sdkRoot">The explicit installed SDK root.</param>
    /// <param name="compilerPath">The optional authoritative installed Clang driver.</param>
    /// <returns>An immutable target pinned to the selected SDK path/version.</returns>
    public static NativeTargetDescriptor Resolve(
        NativeTargetId targetId,
        string sdkRoot,
        string? compilerPath = null
    ) {
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(targetId));
        string family = target.platformId switch { "macos" => "macosx", "ios" => "iphoneos", "ios-simulator" => "iphonesimulator", _ => throw new NotSupportedException("The requested target is not an Apple SDK family.") };
        string root = Path.GetFullPath(sdkRoot);
        Dictionary<string, string> settings = ReadSettings(root);
        if (!settings.TryGetValue("CanonicalName", out string? canonical) || !canonical.StartsWith(family, StringComparison.Ordinal)
            || !settings.TryGetValue("Version", out string? version) || !System.Version.TryParse(version, out _))
            throw new InvalidDataException("The selected Apple SDK does not identify the requested device/simulator family and exact version.");
        string? compiler = compilerPath;
        string includes = Path.Combine(root, "usr", "include"), cxx = Path.Combine(includes, "c++", "v1");
        if (!Directory.Exists(includes)) throw new IOException("The selected Apple SDK has no system include directory.");
        NativeToolchainDescriptor toolchain = new(compiler, root, [includes], cxxSystemIncludeFolders: Directory.Exists(cxx) ? [cxx] : [], sdkVersion: version);
        return new(target.targetId, target.platformId, target.architectureId, target.abiId, target.triple, toolchain, target.capabilities, target.byteOrder);
    }
    /// <summary>Finds existing Apple SDK metadata without executing SDK tools.</summary>
    /// <param name="sdkName">macosx, iphoneos or iphonesimulator.</param>
    /// <returns>The explicitly selected or installed SDK root, or null.</returns>
    public static string? FindInstalledRoot(string sdkName)
    {
        string? configured = Environment.GetEnvironmentVariable("SDKROOT");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) return Path.GetFullPath(configured);
        string family = sdkName switch { "macosx" => "MacOSX", "iphoneos" => "iPhoneOS", "iphonesimulator" => "iPhoneSimulator", _ => throw new ArgumentException("Unknown Apple SDK family.", nameof(sdkName)) };
        string[] roots = ["/Library/Developer/CommandLineTools/SDKs", "/Applications/Xcode.app/Contents/Developer/Platforms/" + family + ".platform/Developer/SDKs"];
        return roots.Where(Directory.Exists).SelectMany(path => Directory.GetDirectories(path, family + "*.sdk")).OrderDescending(StringComparer.Ordinal).FirstOrDefault();
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
}
