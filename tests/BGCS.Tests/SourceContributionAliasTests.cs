using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Tests;

public sealed class SourceContributionAliasTests
{
    [Fact]
    public async Task SelectedTemporaryAliasUsesPhysicalOwnedStoreAndCleansItsExactFiles()
    {
        using TemporaryAlias temporary = await TemporaryAlias.CreateAsync();
        string? generated = null;
        await using BindingHost host = Host(new ObservingFrontend(path => { generated = path; return Task.CompletedTask; }));

        GenerationResult result = await host.GenerateAsync(new(Configuration(), temporary.project));

        Assert.True(result.success, result.GetDiagnosticSummary());
        Assert.True(result.published);
        Assert.NotNull(generated);
        Assert.StartsWith(Path.Combine(temporary.first, "BGCS", "source-inputs", "v1") + Path.DirectorySeparatorChar, generated, StringComparison.Ordinal);
        Assert.Equal(PhysicalPathResolver.Resolve(generated), generated);
        Assert.False(File.Exists(generated));
        var header = Assert.Single(result.artifacts, artifact => artifact.relativePath.EndsWith("/native/include/contributions/alias.source/generated.h", StringComparison.Ordinal));
        Assert.Equal(Contributor.C_HEADER, File.ReadAllText(Path.Combine(result.outputRoot, header.relativePath)));
    }

    [Fact]
    public async Task OwnedSubtreeLinkIsStillRejectedBeforeParsingOrFollowingItsTarget()
    {
        using TemporaryAlias temporary = await TemporaryAlias.CreateAsync();
        string owned = Path.Combine(temporary.first, "BGCS");
        await temporary.LinkAsync(owned, temporary.second);
        bool parsed = false;
        await using BindingHost host = Host(new ObservingFrontend(_ => { parsed = true; return Task.CompletedTask; }));

        GenerationResult result = await host.GenerateAsync(new(Configuration(), temporary.project));

        Assert.False(result.success);
        Assert.False(parsed);
        Assert.Contains("symbolic link or reparse point", result.GetDiagnosticSummary(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(temporary.second));
        Assert.False(Directory.Exists(result.outputRoot));
    }

    [Fact]
    public async Task RetargetingSelectedTemporaryAliasCannotPublishCapturedSources()
    {
        using TemporaryAlias temporary = await TemporaryAlias.CreateAsync();
        bool parsed = false;
        await using BindingHost host = Host(new ObservingFrontend(async _ => { parsed = true; await temporary.RetargetAsync(); }));

        GenerationResult result = await host.GenerateAsync(new(Configuration(), temporary.project));

        Assert.True(parsed);
        Assert.False(result.success);
        Assert.False(result.published);
        Assert.Contains("temporary root changed its physical identity", result.GetDiagnosticSummary(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(result.outputRoot));
        Assert.Empty(Directory.GetFileSystemEntries(temporary.second));
    }

    private static BindingHost Host(IFrontend frontend) => BindingHost.CreateBuilder().AddBuiltIns().WithFrontendFactory(() => frontend)
        .AddSourceContributor("alias.source", 0, () => new Contributor()).Build();

    private static BindingConfiguration Configuration() => new()
    {
        schemaVersion = 1, library = new() { id = "temp-alias", nativeName = "temp-alias" },
        source = new() { headers = ["original.h"], parseMacros = false }, targets = [new() { id = "emscripten-wasm32-emscripten" }],
        api = new() { @namespace = "Generated.TemporaryAlias", name = "NativeApi" }, cache = new() { enabled = false }
    };

    private sealed class Contributor : ISourceContributor
    {
        internal const string C_HEADER = "#pragma once\nint contributed(void);\n";
        public string id => "alias.source";
        public int priority => 0;
        public ValueTask<SourceContribution> ContributeAsync(SourceContributionRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult(new SourceContribution([SourceContributionArtifact.FromUtf8("generated.h", SourceContributionRole.PublicHeader,
                "text/x-c-header", C_HEADER, SourceLanguage.C, exposeToBindings: true)], new(EvidenceLevel.Declared, "temporary-alias fixture")));
    }

    private sealed class ObservingFrontend(Func<string, Task> observe) : IFrontend
    {
        public async ValueTask<FrontendResult> ParseAsync(FrontendRequest request, CancellationToken cancellationToken)
        {
            string generated = Assert.Single(request.sourcePathMappings, pair => pair.Value == "contributions/alias.source/generated.h").Key;
            Assert.Equal(Contributor.C_HEADER, File.ReadAllText(generated));
            await observe(generated);
            NativeType integer = new(new("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
            NativeDeclaration function = new(new("echo"), "Function", "echo", "echo", null, new("original.h", 1, 1))
            { returnType = integer.id, callingConvention = "C" };
            NativeModule module = new(request.target.targetId.value, request.target.triple, 4, [integer], [function], [])
            { language = SourceLanguage.C, byteOrder = request.target.byteOrder };
            return new(module, new([], []));
        }
    }

    private sealed class TemporaryAlias : IDisposable
    {
        private readonly string m_root = Path.Combine(PhysicalPathResolver.Resolve(Path.GetTempPath()), "bgcs-contribution-alias-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, string?> m_environment = new(StringComparer.Ordinal);
        private readonly List<string> m_links = [];
        private string alias => Path.Combine(m_root, "selected-temp");
        internal string first => Path.Combine(m_root, "first");
        internal string second => Path.Combine(m_root, "second");
        internal string project => Path.Combine(m_root, "project");

        internal static async Task<TemporaryAlias> CreateAsync()
        {
            TemporaryAlias temporary = new();
            try
            {
                Directory.CreateDirectory(temporary.first);
                Directory.CreateDirectory(temporary.second);
                Directory.CreateDirectory(temporary.project);
                File.WriteAllText(Path.Combine(temporary.project, "original.h"), "int echo(void);\n");
                await temporary.LinkAsync(temporary.alias, temporary.first);
                foreach (string name in new[] { "TMP", "TEMP", "TMPDIR" })
                {
                    temporary.m_environment.Add(name, Environment.GetEnvironmentVariable(name));
                    Environment.SetEnvironmentVariable(name, temporary.alias);
                }
                Assert.Equal(Path.TrimEndingDirectorySeparator(temporary.alias), Path.TrimEndingDirectorySeparator(Path.GetTempPath()));
                return temporary;
            }
            catch { temporary.Dispose(); throw; }
        }

        internal async Task RetargetAsync()
        {
            DeleteAlias(alias);
            m_links.Remove(alias);
            await LinkAsync(alias, second);
        }

        internal async Task LinkAsync(string path, string target)
        {
            Assert.StartsWith(m_root + Path.DirectorySeparatorChar, Path.GetFullPath(path), StringComparison.Ordinal);
            Assert.StartsWith(m_root + Path.DirectorySeparatorChar, Path.GetFullPath(target), StringComparison.Ordinal);
            if (OperatingSystem.IsWindows())
            {
                string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                string command = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + path.Replace("'", "''", StringComparison.Ordinal)
                    + "' -Target '" + target.Replace("'", "''", StringComparison.Ordinal) + "' | Out-Null";
                var result = await ProcessExecutor.ExecuteAsync(executable,
                    ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command))],
                    m_root, TimeSpan.FromSeconds(30));
                if (new DirectoryInfo(path).LinkTarget is not null) m_links.Add(path);
                Assert.True(result.exitCode == 0, result.standardOutput + Environment.NewLine + result.standardError);
                Assert.Contains(path, m_links);
            }
            else { Directory.CreateSymbolicLink(path, target); m_links.Add(path); }
        }

        private static void DeleteAlias(string path) { if (OperatingSystem.IsWindows()) Directory.Delete(path); else File.Delete(path); }

        public void Dispose()
        {
            foreach ((string name, string? value) in m_environment) Environment.SetEnvironmentVariable(name, value);
            for (int index = m_links.Count - 1; index >= 0; index--) DeleteAlias(m_links[index]);
            if (Directory.Exists(m_root)) Directory.Delete(m_root, recursive: true);
        }
    }
}
