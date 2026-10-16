using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Workers;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeGeneratedInputContractTests
{
    [Theory]
    [InlineData("extra-file")]
    [InlineData("empty-directory")]
    [InlineData("content")]
    [InlineData("missing")]
    public async Task PortableGeneratedDirectoryLocksRejectEveryUndeclaredChangeBeforePlanning(string mutation)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs generated input contract 验收 & " + Guid.NewGuid().ToString("N"));
        string generated = Path.Combine(root, "generated");
        Directory.CreateDirectory(generated);
        try
        {
            const string source = "int native_fixture(int value) { return value + 1; }\n";
            byte[] bytes = Encoding.UTF8.GetBytes(source);
            ContentDigest digest = new(Convert.ToHexStringLower(SHA256.HashData(bytes)));
            string input = Path.Combine(generated, "fixture.c"), manifestPath = Path.Combine(root, "native.manifest.json");
            await File.WriteAllBytesAsync(input, bytes);
            const string portable = "artifact:/generated/fixture.c";
            InputDependency dependency = new(portable, portable, bytes.Length, digest);
            NativeBuildManifest manifest = new("generated-lock", "generated_lock", ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.C),
                [new(portable, SourceLanguage.C, "c17")], [new ArtifactDescriptor("generated/fixture.c", "text/x-c", digest)], [dependency], ["native_fixture"])
            {
                generatedDirectories = [new("contributor/native-source", "artifact:/generated", [new("fixture.c", bytes.Length, digest)])],
                extensionInputs = [new([dependency], []) { absentInputs = [new("artifact:/generated/optional.h", "artifact:/generated/optional.h")] }]
            };
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, WorkerProtocol.jsonOptions));
            await using BindingHost host = BindingHost.CreateDefault();
            ManifestNativeBuildPlanResult valid = await host.PlanNativeBuildAsync(new(manifestPath));
            Assert.True(valid.success, valid.GetDiagnosticSummary());
            Assert.NotNull(valid.plan);
            Assert.Contains(valid.plan.commands, command => command.inputFiles.Contains(input));
            switch (mutation)
            {
                case "extra-file": await File.WriteAllTextAsync(Path.Combine(generated, "injected.h"), "#define ALTERED 1\n"); break;
                case "empty-directory": Directory.CreateDirectory(Path.Combine(generated, "unexpected")); break;
                case "content": await File.AppendAllTextAsync(input, "// changed bytes\n"); break;
                case "missing": File.Delete(input); break;
            }
            ManifestNativeBuildPlanResult rejected = await host.PlanNativeBuildAsync(new(manifestPath));
            Assert.False(rejected.success);
            Assert.Null(rejected.plan);
            Assert.Contains("generated input", rejected.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(root, ".bgcs-native-build")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
