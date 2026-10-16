using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Collections.Immutable;
using System.Text.Json;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Infrastructure.Workers;
using BGCS.Infrastructure.Execution;
using Xunit;

namespace BGCS.MSBuild.Tests;

public sealed class TaskHostTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ActualNetTaskHost_GeneratesOnlyWhenExplicitlyEnabledOutsideDesignTime(bool enabled, bool designTime)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string repository = directory?.FullName ?? throw new InvalidOperationException("The test repository is unavailable.");
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        string task = Path.Combine(repository, "integrations", "BGCS.MSBuild", "bin", configuration, "net10.0", "BGCS.MSBuild.dll");
        string worker = Path.Combine(repository, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
        string props = Path.Combine(repository, "integrations", "BGCS.MSBuild", "buildTransitive", "BGCS.props");
        string targets = Path.Combine(repository, "integrations", "BGCS.MSBuild", "buildTransitive", "BGCS.targets");
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-msbuild-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string? assetManifest = null;
            string workerSource = worker;
            if (enabled && !designTime)
            {
                string hostRid = RuntimeInformation.RuntimeIdentifier;
                workerSource = Path.Combine(temporary, "worker-source");
                Directory.CreateDirectory(workerSource);
                foreach (string common in Directory.EnumerateFiles(worker).Where(file => file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".deps.json", StringComparison.Ordinal) || file.EndsWith(".runtimeconfig.json", StringComparison.Ordinal)))
                    File.Copy(common, Path.Combine(workerSource, Path.GetFileName(common)));
                string nativeSource = Path.Combine(worker, "runtimes", hostRid, "native");
                if (!Directory.Exists(nativeSource))
                {
                    string? explicitNative = Environment.GetEnvironmentVariable("BGCS_CLANG_RUNTIME_DIR");
                    if (hostRid != "osx-x64" || string.IsNullOrWhiteSpace(explicitNative) || !Directory.Exists(explicitNative))
                        throw new DirectoryNotFoundException("The worker test requires built host-native assets or an explicit accepted Intel macOS parser closure.");
                    nativeSource = Path.GetFullPath(explicitNative);
                }
                string nativeDestination = Path.Combine(workerSource, "runtimes", hostRid, "native");
                Directory.CreateDirectory(nativeDestination);
                string extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
                foreach (string native in Directory.EnumerateFiles(nativeSource, "*" + extension))
                    File.Copy(native, Path.Combine(nativeDestination, Path.GetFileName(native)));
                foreach (string required in new[] { "libclang", "libClangSharp" })
                    Assert.True(File.Exists(Path.Combine(nativeDestination, required + extension)), "The accepted parser closure is missing " + required + extension);
                var files = Directory.EnumerateFiles(workerSource).Concat(Directory.EnumerateFiles(nativeDestination));
                WorkerAssetManifest manifest = new(1, hostRid, WorkerProtocol.C_PARSER_IDENTITY, files.Select(file =>
                {
                    using FileStream stream = File.OpenRead(file);
                    return new ArtifactDescriptor(Path.GetRelativePath(workerSource, file).Replace('\\', '/'), "application/octet-stream", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(stream))));
                }).ToImmutableArray());
                assetManifest = Path.Combine(temporary, "worker-source.manifest.json");
                await File.WriteAllTextAsync(assetManifest, JsonSerializer.Serialize(manifest));
            }
            string declaration = "#if defined(_WIN32)\n#define EXPORT __declspec(dllexport)\n#else\n#define EXPORT __attribute__((visibility(\"default\")))\n#endif\nEXPORT int echo(int value);\n";
            await File.WriteAllTextAsync(Path.Combine(temporary, "native.h"), declaration);
            await File.WriteAllTextAsync(Path.Combine(temporary, "native.c"), "#include \"native.h\"\nEXPORT int echo(int value) { return value; }\nEXPORT int added_after_warm_build(void) { return 19; }\n");
            string nativeBuild = enabled && !designTime
                ? ",\"nativeBuild\":{\"mode\":\"sources\",\"provider\":\"" + (OperatingSystem.IsWindows() ? "msvc" : "auto") + "\",\"sources\":[\"native.c\"]}"
                : string.Empty;
            await File.WriteAllTextAsync(Path.Combine(temporary, "bindgen.json"), "{\"schemaVersion\":1,\"library\":{\"id\":\"example\",\"nativeName\":\"example\"},\"source\":{\"language\":\"c\",\"headers\":[\"native.h\"]},\"api\":{\"namespace\":\"Generated.Example\"},\"runtime\":{\"mode\":\"source\"},\"cache\":{\"enabled\":false}" + nativeBuild + "}");
            string source = enabled && !designTime ? "public static class Consumer { public static int Read(Generated.Example.NativeApi api) => api.Echo(17); }" : "public static class Consumer { public static int Read() => 17; }";
            await File.WriteAllTextAsync(Path.Combine(temporary, "Consumer.cs"), source);
            XElement project = new("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), new XElement("Import", new XAttribute("Project", props)),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("AllowUnsafeBlocks", "true"),
                    new XElement("TreatWarningsAsErrors", "true"), new XElement("BGCSGenerateBindings", enabled ? "true" : "false"),
                    new XElement("BGCSMSBuildTaskAssembly", task), new XElement("BGCSWorkerAssetSourceRoot", assetManifest is null ? "" : workerSource),
                    new XElement("BGCSWorkerAssetManifest", assetManifest ?? "")),
                new XElement("Import", new XAttribute("Project", targets)));
            string path = Path.Combine(temporary, "Consumer.csproj");
            new XDocument(project).Save(path);
            string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            string dotnet = string.IsNullOrEmpty(root) ? "dotnet" : Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            var result = await ProcessExecutor.ExecuteAsync(dotnet, ["build", path, "-v:minimal", "-m:1", "-nr:false", "-p:UseSharedCompilation=false", "-p:DesignTimeBuild=" + (designTime ? "true" : "false")],
                temporary, TimeSpan.FromSeconds(120));
            Assert.True(result.exitCode == 0, result.standardOutput + Environment.NewLine + result.standardError);
            Assert.Equal(enabled && !designTime, File.Exists(Path.Combine(temporary, "obj", "Debug", "net10.0", "bgcs", ".bgcs-artifacts.json")));
            Assert.Equal(enabled && !designTime, File.Exists(Path.Combine(temporary, "bin", "Debug", "net10.0", "bgcs", "worker", "worker-assets.manifest.json")));
            if (enabled && !designTime)
            {
                string nativeName = OperatingSystem.IsWindows() ? "example.dll" : OperatingSystem.IsMacOS() ? "libexample.dylib" : "libexample.so";
                Assert.True(File.Exists(Path.Combine(temporary, "bin", "Debug", "net10.0", nativeName)), "MSBuild omitted the verified native library from consumer output.");
                string[] arguments = ["build", path, "-v:minimal", "-m:1", "-nr:false", "-p:UseSharedCompilation=false"];
                var second = await ProcessExecutor.ExecuteAsync(dotnet, arguments, temporary, TimeSpan.FromSeconds(120));
                Assert.True(second.exitCode == 0, second.standardOutput + Environment.NewLine + second.standardError);
                string generated = Directory.GetFiles(Path.Combine(temporary, "obj"), "*.g.cs", SearchOption.AllDirectories)
                    .First(file => file.Contains(Path.DirectorySeparatorChar + "bgcs" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
                string consumerAssembly = Path.Combine(temporary, "bin", "Debug", "net10.0", "Consumer.dll");
                DateTime sourceTime = File.GetLastWriteTimeUtc(generated);
                DateTime assemblyTime = File.GetLastWriteTimeUtc(consumerAssembly);
                var third = await ProcessExecutor.ExecuteAsync(dotnet, arguments, temporary, TimeSpan.FromSeconds(120));
                Assert.True(third.exitCode == 0, third.standardOutput + Environment.NewLine + third.standardError);
                Assert.Equal(sourceTime, File.GetLastWriteTimeUtc(generated));
                Assert.Equal(assemblyTime, File.GetLastWriteTimeUtc(consumerAssembly));
                await File.WriteAllTextAsync(Path.Combine(temporary, "native.h"), declaration + "EXPORT int added_after_warm_build(void);\n");
                var changed = await ProcessExecutor.ExecuteAsync(dotnet, arguments, temporary, TimeSpan.FromSeconds(120));
                Assert.True(changed.exitCode == 0, changed.standardOutput + Environment.NewLine + changed.standardError);
                Assert.Contains(Directory.GetFiles(Path.Combine(temporary, "obj"), "*.cs", SearchOption.AllDirectories),
                    file => File.ReadAllText(file).Contains("AddedAfterWarmBuild", StringComparison.Ordinal));
                var clean = await ProcessExecutor.ExecuteAsync(dotnet, ["clean", path, "-v:minimal", "-m:1", "-nr:false"], temporary, TimeSpan.FromSeconds(120));
                Assert.True(clean.exitCode == 0, clean.standardOutput + Environment.NewLine + clean.standardError);
                string owned = Path.Combine(temporary, "obj", "Debug", "net10.0", "bgcs");
                Assert.Empty(Directory.GetFiles(owned, "*.cs", SearchOption.AllDirectories));
                Assert.True(File.Exists(Path.Combine(owned, ".bgcs-artifacts.json")), "Clean must retain a valid complete empty ownership marker.");
                Assert.False(File.Exists(Path.Combine(temporary, "bin", "Debug", "net10.0", "bgcs", "worker", "worker-assets.manifest.json")));
                var rebuilt = await ProcessExecutor.ExecuteAsync(dotnet, arguments, temporary, TimeSpan.FromSeconds(120));
                Assert.True(rebuilt.exitCode == 0, rebuilt.standardOutput + Environment.NewLine + rebuilt.standardError);
                Assert.True(File.Exists(Path.Combine(temporary, "bin", "Debug", "net10.0", "bgcs", "worker", "worker-assets.manifest.json")));
                Assert.True(File.Exists(Path.Combine(temporary, "bin", "Debug", "net10.0", nativeName)));
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
