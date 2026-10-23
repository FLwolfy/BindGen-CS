using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using Xunit;

namespace BGCS.Tests;

public sealed class SiblingHeaderRelocationTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task RealWorkerGeneratesIdenticalArraysAndLayoutGuardsAfterCheckoutRelocation(BindingImportMode mode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-sibling-generation-" + Guid.NewGuid().ToString("N"));
        try
        {
            string[] checkouts = [Path.Combine(root, "original checkout"), Path.Combine(root, "a longer relocated checkout")];
            string[][] generated = new string[checkouts.Length][];
            for (int index = 0; index < checkouts.Length; index++)
            {
                string checkout = checkouts[index], configurationRoot = Path.Combine(checkout, "configuration");
                Directory.CreateDirectory(configurationRoot);
                foreach (string name in new[] { "Left", "Right" })
                {
                    string directory = Path.Combine(checkout, "headers", name);
                    Directory.CreateDirectory(directory);
                    await File.WriteAllTextAsync(Path.Combine(directory, "common.h"),
                        "typedef struct " + name + " { struct { int value; } items[3]; } " + name + ";\n");
                }
                await File.WriteAllTextAsync(Path.Combine(checkout, "headers", "native.h"),
                    "#include \"Left/common.h\"\n#include \"Right/common.h\"\n#define REPLACED 0\n#undef REPLACED\n#define REPLACED 64\n#define REMOVED 1\n#undef REMOVED\nvoid Observe(Left* left, Right* right);\n");
                BindingConfiguration configuration = new()
                {
                    schemaVersion = 2,
                    library = new() { id = "sibling", nativeName = "sibling" },
                    source = new() { headers = ["../headers/native.h"], includeDirectories = ["../headers"] },
                    api = new()
                    {
                        @namespace = "Generated.Siblings",
                        name = "SiblingApi",
                        importMode = mode,
                        names = ImmutableDictionary<string, string>.Empty.Add("REPLACED", "ObservedValue").Add("REMOVED", "RemovedValue")
                    },
                    cache = new() { enabled = false }
                };
                await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
                GenerationResult result = await host.GenerateAsync(new(configuration, configurationRoot));
                Assert.True(result.success, result.GetDiagnosticSummary());
                generated[index] = result.artifacts.Where(artifact => artifact.relativePath.EndsWith(".cs", StringComparison.Ordinal))
                    .OrderBy(artifact => artifact.relativePath, StringComparer.Ordinal)
                    .Select(artifact => artifact.relativePath + "\n" + File.ReadAllText(Path.Combine(result.outputRoot, artifact.relativePath))).ToArray();
                Assert.NotEmpty(generated[index]);
                Assert.Contains(generated[index], source => source.Contains("NativeArray_", StringComparison.Ordinal));
                Assert.Contains(generated[index], source => source.Contains("__BgcsAlignment_", StringComparison.Ordinal));
                Assert.Contains(generated[index], source => source.Contains("public const int ObservedValue = 64;", StringComparison.Ordinal));
                Assert.DoesNotContain(generated[index], source => source.Contains("public const int RemovedValue", StringComparison.Ordinal));
            }
            Assert.Equal(generated[0], generated[1]);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string repository = directory?.FullName ?? throw new InvalidOperationException("The repository root is unavailable.");
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
