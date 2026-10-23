using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Infrastructure.Verification;
using BGCS.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Configuration.Tests;

/// <summary>Exercises explicitly authored grouped fixtures through the public Host and exact published artifacts.</summary>
public abstract class GroupedConfigurationEntryTestBase
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    protected static async Task VerifyAsync(
        string fixture,
        string variant
    ) {
        string input = Path.Combine(AppContext.BaseDirectory, "groupedEntryTests", fixture);
        Assert.True(Directory.Exists(input), "Grouped fixture was not deployed: " + input);
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-grouped-entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (string file in Directory.GetFiles(input, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(temporary, Path.GetRelativePath(input, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            JsonObject expected = JsonNode.Parse(File.ReadAllText(Path.Combine(temporary, variant + ".expected.json")))!.AsObject();
            string currentDirectory = Environment.CurrentDirectory;
            GenerationRequest request = await TestInputs.LoadRequestAsync(Path.Combine(temporary, variant + ".bindgen.json"));
            Assert.Equal(temporary, request.baseDirectory);
            Assert.Equal(currentDirectory, Environment.CurrentDirectory);
            VerifyConfiguration(request.configuration, expected["configuration"]!.AsObject(), temporary);
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            bool sdkBuild = expected["sdkBuild"]?.GetValue<bool>() == true || request.configuration.api.importMode == BindingImportMode.LibraryImport;
            GenerationResult result = await host.GenerateAsync(request);
            Assert.Equal(currentDirectory, Environment.CurrentDirectory);
            bool success = expected["success"]?.GetValue<bool>() ?? true;
            if (!success)
            {
                Assert.False(result.success, result.GetDiagnosticSummary());
                Assert.False(result.published);
                Assert.False(Directory.Exists(request.configuration.output.root));
                foreach (JsonNode? reason in expected["diagnosticsContain"]!.AsArray())
                    Assert.Contains(reason!.GetValue<string>(), result.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
                return;
            }
            Assert.True(result.success, result.GetDiagnosticSummary());
            new ArtifactDigestVerifier(TestHostPaths.Create()).Verify(result.outputRoot, result.artifacts, [".bgcs-artifacts.json"]);
            var sources = result.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp").OrderBy(artifact => artifact.relativePath, StringComparer.Ordinal).ToArray();
            Assert.NotEmpty(sources);
            string[] texts = sources.Select(artifact => File.ReadAllText(Path.Combine(result.outputRoot, artifact.relativePath))).ToArray();
            string bindings = string.Join(Environment.NewLine, texts);
            foreach (JsonNode? snippet in expected["contains"]?.AsArray() ?? [])
                Assert.Contains(snippet!.GetValue<string>(), bindings, StringComparison.Ordinal);
            foreach (JsonNode? snippet in expected["notContains"]?.AsArray() ?? [])
                Assert.DoesNotContain(snippet!.GetValue<string>(), bindings, StringComparison.Ordinal);
            if (expected["singleFile"]?.GetValue<bool>() is bool singleFile)
            {
                if (singleFile)
                    Assert.EndsWith("/managed/Bindings.cs", Assert.Single(sources).relativePath, StringComparison.Ordinal);
                else
                    Assert.True(sources.Length > 1, "Split output must contain distinct manifest-owned source files.");
            }
            if (sdkBuild)
                await CompileSdkConsumerAsync(request.configuration, sources.Select(source => Path.Combine(result.outputRoot, source.relativePath)), temporary);
            CSharpCompilation compilation = Compile(request.configuration, sources.Select(source => source.relativePath).ToArray(), texts, sdkBuild);
            VerifySymbols(compilation, expected);
            new ArtifactDigestVerifier(TestHostPaths.Create()).Verify(result.outputRoot, result.artifacts, [".bgcs-artifacts.json"]);
        }
        finally
        {
            string resolved = Path.GetFullPath(temporary);
            string ownedPrefix = Path.Combine(Path.GetTempPath(), "bgcs-grouped-entry-");
            if (!resolved.StartsWith(ownedPrefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidOperationException("Grouped fixture cleanup path escaped its owned temporary directory.");
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static async Task CompileSdkConsumerAsync(
        BindingConfiguration configuration,
        IEnumerable<string> generatedSources,
        string temporary
    ) {
        string consumer = Path.Combine(temporary, "SdkConsumer");
        Directory.CreateDirectory(consumer);
        string[] sources = generatedSources.Concat(configuration.api.projectionInputs.sources.Select(path => Path.GetFullPath(path, temporary)))
            .Distinct(StringComparer.Ordinal).ToArray();
        string[] references = configuration.api.projectionInputs.references.Select(path => Path.GetFullPath(path, temporary))
            .Concat(configuration.runtime.mode == "source" ? [] : new[] { typeof(NativeModuleSession).Assembly.Location }).Distinct(StringComparer.Ordinal).ToArray();
        string compile = string.Join(Environment.NewLine, sources.Select(path => "<Compile Include=\"" + SecurityElement.Escape(path) + "\" />"));
        string metadata = string.Join(Environment.NewLine, references.Select((
            path,
            index
        ) => "<Reference Include=\"FixtureReference" + index + "\"><HintPath>" + SecurityElement.Escape(path) + "</HintPath></Reference>"));
        await File.WriteAllTextAsync(Path.Combine(consumer, "Consumer.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{SecurityElement.Escape(configuration.api.framework)}}</TargetFramework>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                <DefineConstants>$(DefineConstants);{{SecurityElement.Escape(string.Join(';', configuration.api.projectionInputs.defines))}}</DefineConstants>
              </PropertyGroup>
              <ItemGroup>{{compile}}{{metadata}}</ItemGroup>
            </Project>
            """);
        string? sdk = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        string dotnet = string.IsNullOrWhiteSpace(sdk) ? "dotnet" : Path.Combine(sdk, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        ProcessStartInfo start = new(dotnet)
        {
            WorkingDirectory = consumer,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in new[] { "build", "Consumer.csproj", "-c", "Release", "-v:minimal", "-m:1", "-nr:false", "-p:UseSharedCompilation=false" }) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the grouped fixture SDK consumer.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(deadline.Token); }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
        string standardOutput = await output;
        string standardError = await error;
        string buildOutput = standardOutput + Environment.NewLine + standardError;
        Assert.True(process.ExitCode == 0, buildOutput);
        string assembly = Path.Combine(consumer, "bin", "Release", configuration.api.framework, "Consumer.dll");
        Assert.True(File.Exists(assembly), "The real SDK did not produce the grouped consumer assembly: " + assembly + Environment.NewLine + buildOutput);
    }

    private static void VerifyConfiguration(
        BindingConfiguration configuration,
        JsonObject expected,
        string temporary
    ) {
        JsonNode actual = JsonSerializer.SerializeToNode(configuration, s_json)!;
        foreach ((string pointer, JsonNode? value) in expected)
        {
            JsonNode? selected = actual;
            Assert.StartsWith("/", pointer, StringComparison.Ordinal);
            foreach (string segment in pointer[1..].Split('/'))
                selected = selected?[segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)];
            JsonNode? normalized = value?.DeepClone();
            NormalizePaths(normalized, temporary);
            Assert.True(JsonNode.DeepEquals(normalized, selected), $"Grouped configuration {pointer}: expected {normalized}, actual {selected}.");
        }
    }

    private static void NormalizePaths(
        JsonNode? node,
        string temporary
    ) {
        if (node is JsonArray array)
            for (int index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value && value.TryGetValue(out string? text) && text is not null && text.StartsWith("$fixture/", StringComparison.Ordinal))
                    array[index] = Path.GetFullPath(Path.Combine(temporary, text[9..]));
                else NormalizePaths(array[index], temporary);
            }
    }

    private static CSharpCompilation Compile(
        BindingConfiguration configuration,
        string[] paths,
        string[] texts,
        bool sdkBuild
    ) {
        string framework = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string[] trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Framework metadata is unavailable."))
            .Split(Path.PathSeparator).Where(path => Path.GetDirectoryName(path) == framework).ToArray();
        IEnumerable<string> references = configuration.runtime.mode == "source" ? trusted : trusted.Append(typeof(NativeModuleSession).Assembly.Location);
        CSharpCompilation compilation = CSharpCompilation.Create("Grouped.Entry." + Guid.NewGuid().ToString("N"),
            paths.Select((
                path,
                index
            ) => CSharpSyntaxTree.ParseText(texts[index], new CSharpParseOptions(LanguageVersion.CSharp14), path)),
            references.Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));
        // LibraryImport's implementation must be produced by the real SDK source generator in the test-owned consumer above.
        if (configuration.api.importMode == BindingImportMode.LibraryImport)
            Assert.True(sdkBuild, "LibraryImport fixtures require a real SDK build, never incomplete Roslyn-only compilation.");
        else
        {
            using MemoryStream image = new();
            var emission = compilation.Emit(image);
            Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        }
        return compilation;
    }

    private static void VerifySymbols(
        CSharpCompilation compilation,
        JsonObject expected
    ) {
        foreach (JsonNode? item in expected["absentTypes"]?.AsArray() ?? [])
            Assert.Null(compilation.GetTypeByMetadataName(item!.GetValue<string>()));
        foreach (JsonNode? item in expected["types"]?.AsArray() ?? [])
        {
            INamedTypeSymbol? type = compilation.GetTypeByMetadataName(item!["name"]!.GetValue<string>());
            Assert.NotNull(type);
            if (item["kind"] is JsonNode kind) Assert.Equal(kind.GetValue<string>(), type.TypeKind.ToString());
            if (item["isStatic"] is JsonNode isStatic) Assert.Equal(isStatic.GetValue<bool>(), type.IsStatic);
        }
        foreach (JsonNode? item in expected["methods"]?.AsArray() ?? [])
        {
            INamedTypeSymbol type = compilation.GetTypeByMetadataName(item!["type"]!.GetValue<string>()) ?? throw new InvalidOperationException("Expected method owner is missing.");
            string[] parameterTypes = item["parameters"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
            IMethodSymbol method = Assert.Single(type.GetMembers(item["name"]!.GetValue<string>()).OfType<IMethodSymbol>(),
                candidate => candidate.Parameters.Select(parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)).SequenceEqual(parameterTypes));
            Assert.Equal(Accessibility.Public, method.DeclaredAccessibility);
            Assert.Equal(item["returns"]!.GetValue<string>(), method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            Assert.Equal(item["isStatic"]?.GetValue<bool>() ?? false, method.IsStatic);
            if (item["parameterNames"] is JsonArray names)
                Assert.Equal(names.Select(name => name!.GetValue<string>()), method.Parameters.Select(parameter => parameter.Name));
            if (item["parameterRefKinds"] is JsonArray kinds)
                Assert.Equal(kinds.Select(kind => kind!.GetValue<string>()), method.Parameters.Select(parameter => parameter.RefKind.ToString()));
        }
        foreach (JsonNode? item in expected["constants"]?.AsArray() ?? [])
        {
            INamedTypeSymbol type = compilation.GetTypeByMetadataName(item!["type"]!.GetValue<string>()) ?? throw new InvalidOperationException("Expected constant owner is missing.");
            IFieldSymbol field = Assert.Single(type.GetMembers(item["name"]!.GetValue<string>()).OfType<IFieldSymbol>());
            Assert.True(field.HasConstantValue);
            Assert.True(JsonNode.DeepEquals(item["value"], JsonSerializer.SerializeToNode(field.ConstantValue)), "Constant value changed: " + field.ToDisplayString());
        }
        foreach (JsonNode? item in expected["absentMembers"]?.AsArray() ?? [])
        {
            INamedTypeSymbol? type = compilation.GetTypeByMetadataName(item!["type"]!.GetValue<string>());
            if (type is null && item["ownerOptional"]?.GetValue<bool>() == true) continue;
            Assert.NotNull(type);
            Assert.Empty(type.GetMembers(item["name"]!.GetValue<string>()));
        }
    }

    private static string WorkerRoot([CallerFilePath] string source = "")
    {
        DirectoryInfo? repository = new(Path.GetDirectoryName(source)!);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        // SourceLink may remap CallerFilePath; the test output is still under the repository checkout.
        repository ??= new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        Assert.NotNull(repository);
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        string root = Path.Combine(repository.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
        Assert.True(File.Exists(Path.Combine(root, "BGCS.Frontend.Clang.Worker.dll")), "Build the isolated worker before running grouped configuration fixtures.");
        return root;
    }
}
