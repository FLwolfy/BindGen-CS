using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace BGCS.ApiSnapshot;

internal sealed class SnapshotLoadContext : AssemblyLoadContext, IDisposable
{
    private readonly AssemblyDependencyResolver resolver;
    private readonly Dictionary<string, string> packageAssemblies;

    public SnapshotLoadContext(string assemblyPath) : this(
        assemblyPath,
        Environment.GetEnvironmentVariable("NUGET_PACKAGES") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"))
    {
    }

    internal SnapshotLoadContext(string assemblyPath, string packagesRoot) : base(isCollectible: true)
    {
        resolver = new AssemblyDependencyResolver(assemblyPath);
        packageAssemblies = ReadPackageAssemblies(Path.ChangeExtension(assemblyPath, ".deps.json"), packagesRoot);
    }

    internal string? ResolveDependencyPath(AssemblyName assemblyName)
    {
        // Framework assemblies must come from the running framework, never a NuGet cache.
        if (assemblyName.Name == null ||
            File.Exists(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, assemblyName.Name + ".dll")))
            return null;

        string? resolved = resolver.ResolveAssemblyToPath(assemblyName);
        if (resolved != null)
            return resolved;

        return ResolvePinnedPackagePath(packageAssemblies, assemblyName.Name);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string? path = ResolveDependencyPath(assemblyName);
        if (path == null)
            return null;

        AssemblyName selected = AssemblyName.GetAssemblyName(path);
        if (!AssemblyName.ReferenceMatchesDefinition(selected, assemblyName) ||
            (assemblyName.Version != null && selected.Version != assemblyName.Version))
            throw new FileLoadException($"Snapshot dependency '{assemblyName}' resolved to incompatible assembly '{selected}' at '{path}'.");
        return LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        string? path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path == null ? 0 : LoadUnmanagedDllFromPath(path);
    }

    public void Dispose() => Unload();

    internal static string? ResolvePinnedPackagePath(IReadOnlyDictionary<string, string> assemblies, string? assemblyName) =>
        assemblyName != null && assemblies.TryGetValue(assemblyName, out string? path) && File.Exists(path) ? path : null;

    internal static Dictionary<string, string> ReadPackageAssemblies(string depsPath, string packagesRoot)
    {
        Dictionary<string, string> assemblies = new(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(depsPath))
            return assemblies;

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(depsPath));
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("runtimeTarget", out JsonElement runtimeTarget) ||
            !runtimeTarget.TryGetProperty("name", out JsonElement targetName) ||
            !root.TryGetProperty("targets", out JsonElement targets) ||
            !targets.TryGetProperty(targetName.GetString()!, out JsonElement target) ||
            !root.TryGetProperty("libraries", out JsonElement libraries))
            throw new InvalidDataException($"Invalid dependency manifest: {depsPath}");

        foreach (JsonProperty library in target.EnumerateObject())
        {
            if (!libraries.TryGetProperty(library.Name, out JsonElement metadata) ||
                !metadata.TryGetProperty("type", out JsonElement type) || type.GetString() != "package" ||
                !metadata.TryGetProperty("path", out JsonElement packagePath) ||
                !library.Value.TryGetProperty("runtime", out JsonElement runtimeAssets))
                continue;

            foreach (JsonProperty asset in runtimeAssets.EnumerateObject())
            {
                if (!asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                string assemblyName = Path.GetFileNameWithoutExtension(asset.Name);
                string path = Path.GetFullPath(Path.Combine(packagesRoot,
                    packagePath.GetString()!.Replace('/', Path.DirectorySeparatorChar),
                    asset.Name.Replace('/', Path.DirectorySeparatorChar)));
                if (!assemblies.TryAdd(assemblyName, path))
                    throw new InvalidDataException($"Ambiguous runtime assembly '{assemblyName}' in {depsPath}");
            }
        }
        return assemblies;
    }
}
