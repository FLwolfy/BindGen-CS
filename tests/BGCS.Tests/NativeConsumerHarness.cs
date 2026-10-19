using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.Verification;
using BGCS.Infrastructure.Publishing;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.Tests;

internal static class NativeConsumerHarness
{
    // Test fixtures compile their own sources after exercising the public generator.
    // This is intentionally not a product provider, manifest or packaging pipeline.
    internal static async Task<GenerationResult> GenerateAndCompileAsync(BindingHost host, GenerationRequest request, bool compileManaged = false)
    {
        GenerationResult generated = await host.GenerateAsync(request);
        if (generated.success)
        {
            List<string> arguments = [.. request.configuration.source.arguments];
            arguments.AddRange(request.configuration.source.defines.Select(value => "-D" + value));
            foreach (string include in request.configuration.source.includeDirectories) { arguments.Add("-I"); arguments.Add(Path.GetFullPath(include, request.baseDirectory)); }
            await CompileNativeFixturesAsync(generated, request.baseDirectory, request.configuration.library.nativeName,
                arguments: arguments, languageStandard: request.configuration.source.languageStandard);
            if (compileManaged) await CompileManagedAsync(generated, request);
        }
        return generated;
    }

    internal static async Task CompileManagedAsync(GenerationResult generated, GenerationRequest request)
    {
        Assert.True(generated.success, generated.GetDiagnosticSummary());
        new ArtifactDigestVerifier().Verify(generated.outputRoot, generated.artifacts, [".bgcs-artifacts.json"]);
        foreach (TargetInspection target in generated.targetModels)
        {
            string[] sources = generated.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp"
                    && artifact.relativePath.StartsWith(target.targetId + "/", StringComparison.Ordinal))
                .Select(artifact => Path.Combine(generated.outputRoot, artifact.relativePath))
                .Concat(request.configuration.api.projectionInputs.sources.Select(path => Path.GetFullPath(path, request.baseDirectory)))
                .Distinct(StringComparer.Ordinal).ToArray();
            Assert.NotEmpty(sources);
            string[] references = request.configuration.api.projectionInputs.references.Select(path => Path.GetFullPath(path, request.baseDirectory))
                .Concat(request.configuration.runtime.mode == "source" ? [] : new[] { typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location })
                .Distinct(StringComparer.Ordinal).ToArray();
            string consumer = Path.Combine(Path.GetFullPath(request.configuration.cache.directory, request.baseDirectory), "fixture-consumers",
                Path.GetFileName(generated.outputRoot), target.targetId, "managed-only");
            Directory.CreateDirectory(consumer);
            string compile = string.Join(Environment.NewLine, sources.Select(path => "<Compile Include=\"" + SecurityElement.Escape(path) + "\" />"));
            string metadata = string.Join(Environment.NewLine, references.Select((path, index) => "<Reference Include=\"FixtureReference" + index + "\"><HintPath>" + SecurityElement.Escape(path) + "</HintPath></Reference>"));
            await File.WriteAllTextAsync(Path.Combine(consumer, "Consumer.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>{{SecurityElement.Escape(request.configuration.api.framework)}}</TargetFramework>
                    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                    <Nullable>enable</Nullable>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                    <DefineConstants>$(DefineConstants);{{SecurityElement.Escape(string.Join(';', request.configuration.api.projectionInputs.defines))}}</DefineConstants>
                  </PropertyGroup>
                  <ItemGroup>{{compile}}{{metadata}}</ItemGroup>
                </Project>
                """);
            string? sdk = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            string dotnet = string.IsNullOrWhiteSpace(sdk) ? "dotnet" : Path.Combine(sdk, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            await RunAsync(dotnet, ["build", "Consumer.csproj", "--no-incremental", "-v:minimal", "-m:1", "-nr:false", "-p:UseSharedCompilation=false"], consumer);
            Assert.True(File.Exists(Path.Combine(consumer, "bin", "Debug", request.configuration.api.framework, "Consumer.dll")),
                "The SDK did not produce the fixture consumer assembly.");
        }
        new ArtifactDigestVerifier().Verify(generated.outputRoot, generated.artifacts, [".bgcs-artifacts.json"]);
    }

    internal static async Task CompileNativeFixturesAsync(GenerationResult generated, string sourceRoot, string libraryName,
        IEnumerable<string>? explicitSources = null, IEnumerable<string>? arguments = null, string? languageStandard = null)
    {
        string[] fixtureSources = (explicitSources ?? Directory.GetFiles(sourceRoot).Where(IsSource)).Select(Path.GetFullPath).ToArray();
        foreach (TargetInspection target in generated.targetModels)
        {
            string targetRoot = Path.Combine(generated.outputRoot, target.targetId);
            NativeSourceManifest manifest = Assert.Single(generated.nativeSources, item => item.targetId == target.targetId);
            string[] generatedSources = manifest.sources.Select(source => Path.Combine(generated.outputRoot, source.relativePath)).ToArray();
            Dictionary<string, string> roots = generated.nativeInputRoots.Where(item => item.targetId == target.targetId)
                .ToDictionary(item => item.rootId, item => item.directory, StringComparer.Ordinal);
            string[] declaredSources = manifest.inputs.Where(input => input.role == "ShimSource")
                .Select(input => Path.Combine(roots[input.rootId], input.relativePath)).ToArray();
            string[] sources = fixtureSources.Concat(generatedSources).Concat(declaredSources).Distinct(StringComparer.Ordinal).ToArray();
            if (sources.Length == 0) continue;
            string binaryRoot = NativeDirectory(targetRoot);
            Directory.CreateDirectory(binaryRoot);
            string binary = Path.Combine(binaryRoot, OperatingSystem.IsWindows() ? libraryName + ".dll" : OperatingSystem.IsMacOS() ? "lib" + libraryName + ".dylib" : "lib" + libraryName + ".so");
            List<string> compilerArguments = [.. arguments ?? []];
            foreach (NativeIncludeDirectory include in manifest.includeDirectories.Where(include => include.isSystem))
                compilerArguments.AddRange([include.isFramework ? "-iframework" : "-isystem", Path.Combine(roots[include.rootId], include.relativePath)]);
            await CompileSharedAsync(sources, binary, sourceRoot, compilerArguments,
                manifest.includeDirectories.Where(include => !include.isSystem).Select(include => Path.Combine(roots[include.rootId], include.relativePath)),
                languageStandard ?? manifest.languageStandard, target.resolvedTarget);
        }
        static bool IsSource(string path) => Path.GetExtension(path) is ".c" or ".cpp" or ".cc" or ".cxx";
    }

    internal static async Task CompileSharedAsync(IEnumerable<string> sources, string binary, string sourceRoot,
        IEnumerable<string>? arguments = null, IEnumerable<string>? includeDirectories = null, string? languageStandard = null,
        NativeTargetDescriptor? parserTarget = null)
    {
        string[] sourceFiles = sources.ToArray();
        NativeTargetDescriptor? appleTarget = OperatingSystem.IsMacOS() ? parserTarget ?? ParserTarget(SourceLanguage.Cpp) : null;
        Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
        string intermediates = Path.Combine(Path.GetDirectoryName(binary)!, "obj");
        Directory.CreateDirectory(intermediates);
        List<string> objects = [];
        for (int index = 0; index < sourceFiles.Length; index++)
        {
            bool cpp = Path.GetExtension(sourceFiles[index]) != ".c";
            string output = Path.Combine(intermediates, "fixture-" + index + (OperatingSystem.IsWindows() ? ".obj" : ".o"));
            string standard = languageStandard is not null && languageStandard.StartsWith("c++", StringComparison.Ordinal) == cpp
                ? languageStandard : cpp ? "c++23" : "c17";
            List<string> command = ["-c", sourceFiles[index], "-o", output, "-std=" + standard, "-I", sourceRoot];
            foreach (string include in includeDirectories ?? []) { command.Add("-I"); command.Add(include); }
            command.AddRange(CompilerSdkArguments(cpp ? SourceLanguage.Cpp : SourceLanguage.C, appleTarget));
            if (!OperatingSystem.IsWindows()) command.Add("-fPIC");
            command.AddRange(arguments ?? []);
            await RunAsync(FindCompiler(cpp), command, sourceRoot);
            objects.Add(output);
        }
        List<string> link = [OperatingSystem.IsMacOS() ? "-dynamiclib" : "-shared", .. objects, "-o", binary];
        if (OperatingSystem.IsWindows())
        {
            link.AddRange(["-fuse-ld=lld", "-fms-runtime-lib=dll"]);
            foreach (string path in WindowsLibraries()) link.Add("-L" + path);
        }
        else link.Add("-pthread");
        if (appleTarget?.toolchain.sysRoot is string sysroot) link.AddRange(["-isysroot", sysroot]);
        link.AddRange(arguments ?? []);
        await RunAsync(FindCompiler(true), link, sourceRoot);
        Assert.True(File.Exists(binary), binary);
        foreach (string path in objects) File.Delete(path);
        if (OperatingSystem.IsWindows())
            foreach (string extension in new[] { ".lib", ".exp", ".pdb" }) File.Delete(Path.ChangeExtension(binary, extension));
    }

    internal static string NativeDirectory(string generatedTargetRoot)
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(generatedTargetRoot)!)!, ".bgcs", "cache", "fixture-consumers",
            Path.GetFileName(Path.GetDirectoryName(generatedTargetRoot)!), Path.GetFileName(generatedTargetRoot), "bin");

    internal static string FindCompiler(bool cpp)
    {
        string? configured = Environment.GetEnvironmentVariable(cpp ? "BGCS_CPP2C_CXX" : "BGCS_CC")
            ?? Environment.GetEnvironmentVariable(cpp ? "CXX" : "CC");
        string[] candidates = [configured ?? "", OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LLVM", "bin", cpp ? "clang++.exe" : "clang.exe") : OperatingSystem.IsMacOS() ? cpp ? "/usr/bin/clang++" : "/usr/bin/clang" : "", cpp ? "clang++" : "clang", cpp ? "g++" : "gcc"];
        foreach (string candidate in candidates.Where(value => value.Length != 0))
        {
            if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate)) return candidate;
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                foreach (string name in OperatingSystem.IsWindows() ? new[] { candidate, candidate + ".exe" } : new[] { candidate })
                    if (File.Exists(Path.Combine(directory, name))) return Path.GetFullPath(Path.Combine(directory, name));
        }
        throw new FileNotFoundException("The native test fixture requires an installed C/C++ compiler.");
    }

    internal static NativeTargetDescriptor ParserTarget(SourceLanguage language)
        => ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"))), language);

    internal static IEnumerable<string> CompilerSdkArguments(SourceLanguage language = SourceLanguage.Cpp, NativeTargetDescriptor? parserTarget = null)
    {
        if (OperatingSystem.IsWindows())
        {
            NativeTargetDescriptor target = ParserTarget(SourceLanguage.Cpp);
            foreach (string include in target.toolchain.systemIncludeFolders) { yield return "-isystem"; yield return include; }
            foreach (string argument in target.toolchain.arguments) yield return argument;
            yield return "-fms-runtime-lib=dll";
        }
        else if (OperatingSystem.IsMacOS())
        {
            // The proof must compile against the SDK that supplied its parsed
            // libc++ layout, rather than the selected driver's default SDK.
            // Keep builtin resource headers ahead of SDK C headers: libc++'s
            // stddef.h uses include_next to obtain the compiler's definitions.
            NativeTargetDescriptor target = parserTarget ?? ParserTarget(language);
            if (target.toolchain.sysRoot is string sysroot) { yield return "-isysroot"; yield return sysroot; }
            if (language == SourceLanguage.Cpp && target.toolchain.cxxSystemIncludeFolders.Count != 0)
            {
                // Only the explicitly parsed libc++ installation may supply
                // C++ headers; retain the driver's own builtin resource path.
                yield return "-nostdinc++";
                foreach (string include in target.toolchain.cxxSystemIncludeFolders)
                { yield return "-isystem"; yield return include; }
            }
            foreach (string include in target.toolchain.systemIncludeFolders)
            { yield return "-idirafter"; yield return include; }
            foreach (string define in target.toolchain.defines) yield return "-D" + define;
            foreach (string argument in target.toolchain.arguments) yield return argument;
        }
    }

    private static IEnumerable<string> WindowsLibraries()
    {
        string compiler = MsvcToolchainDiscovery.FindCompiler("x64") ?? throw new FileNotFoundException("The Windows native fixture needs an installed Visual C++ SDK.");
        MsvcToolchainInfo sdk = MsvcToolchainDiscovery.Resolve(compiler, "x64");
        string toolset = Directory.GetParent(Path.GetDirectoryName(compiler)!)!.Parent!.Parent!.FullName;
        yield return Path.Combine(toolset, "lib", "x64");
        yield return Path.Combine(sdk.sdkRoot, "Lib", sdk.sdkVersion, "ucrt", "x64");
        yield return Path.Combine(sdk.sdkRoot, "Lib", sdk.sdkVersion, "um", "x64");
    }

    internal static async Task RunAsync(string executable, IEnumerable<string> arguments, string directory)
    {
        ProcessStartInfo start = new(executable) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start fixture compiler.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(deadline.Token); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        Assert.True(process.ExitCode == 0, await output + Environment.NewLine + await error);
    }

    internal static object? InvokeFactory(this MethodInfo method, object?[] arguments)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (arguments.Length > parameters.Length) throw new ArgumentException("Too many explicit factory arguments.", nameof(arguments));
        object?[] supplied = new object?[parameters.Length];
        Array.Copy(arguments, supplied, arguments.Length);
        for (int index = arguments.Length; index < parameters.Length; ++index)
        {
            if (!parameters[index].IsOptional) throw new ArgumentException("A required factory argument was omitted.", nameof(arguments));
            supplied[index] = Type.Missing;
        }
        return method.Invoke(null, supplied);
    }

    internal static async Task<object?> CompileAndInvokeAsync(string managedRoot, string nativeBinary, string nativeLibraryName,
        string probeSource, string probeType, string root, bool sourceRuntime, IEnumerable<string>? additionalSources = null)
    {
        string targetRoot = Directory.GetParent(managedRoot)!.FullName;
        string outputRoot = Directory.GetParent(targetRoot)!.FullName;
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputRoot, ".bgcs-artifacts.json")));
        var artifacts = new ArtifactPublicationReader().ReadVerified(outputRoot, manifest.RootElement.GetProperty("libraryId").GetString()!);
        string managedPrefix = Path.GetRelativePath(outputRoot, managedRoot).Replace('\\', '/') + "/";
        string consumer = Path.Combine(NativeDirectory(targetRoot), "Consumer");
        Directory.CreateDirectory(consumer);
        string supplied = Path.Combine(root, "Consumer");
        await File.WriteAllTextAsync(Path.Combine(consumer, "Probe.cs"), probeSource);
        string[] sources = artifacts.Where(artifact => artifact.mediaType == "text/x-csharp" && artifact.relativePath.StartsWith(managedPrefix, StringComparison.Ordinal))
            .Select(artifact => Path.Combine(outputRoot, artifact.relativePath))
            .Concat(Directory.Exists(supplied) ? Directory.GetFiles(supplied, "*.cs") : [])
            .Concat((additionalSources ?? []).Select(path => Path.GetFullPath(path, root)))
            .Append(Path.Combine(consumer, "Probe.cs")).Distinct(StringComparer.Ordinal).ToArray();
        string compile = string.Join(Environment.NewLine, sources.Select(path => "<Compile Include=\"" + SecurityElement.Escape(path) + "\" />"));
        string reference = sourceRuntime ? "" : "<Reference Include=\"BGCS.Runtime\"><HintPath>" + SecurityElement.Escape(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location) + "</HintPath></Reference>";
        await File.WriteAllTextAsync(Path.Combine(consumer, "Consumer.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
            <ItemGroup>{{compile}}{{reference}}</ItemGroup></Project>
            """);
        string? sdk = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        ProcessStartInfo start = new(string.IsNullOrEmpty(sdk) ? "dotnet" : Path.Combine(sdk, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"))
        { WorkingDirectory = consumer, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "build", "Consumer.csproj", "-v:minimal", "-m:1", "-nr:false", "-p:UseSharedCompilation=false" }) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(deadline.Token); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        Assert.True(process.ExitCode == 0, await output + Environment.NewLine + await error);
        using FileStream image = File.OpenRead(Path.Combine(consumer, "bin", "Debug", "net10.0", "Consumer.dll"));
        AssemblyLoadContext load = new("BGCS.NativeConsumer." + Guid.NewGuid().ToString("N"), isCollectible: true);
        nint native = NativeLibrary.Load(nativeBinary);
        try
        {
            Assembly assembly = load.LoadFromStream(image);
            NativeLibrary.SetDllImportResolver(assembly, (name, _, _) => name == nativeLibraryName ? native : 0);
            return assembly.GetType(probeType, true)!.GetMethod("Run")!.Invoke(null, [nativeBinary]);
        }
        finally { load.Unload(); NativeLibrary.Free(native); }
    }
}
