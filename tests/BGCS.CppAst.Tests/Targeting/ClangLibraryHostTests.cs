using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.CppAst.Tests.Targeting;

public sealed class ClangLibraryHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParserLoadsItsBundledRuntimeFromAnUnrelatedHostAndRejectsIncompleteOverrides(bool invalidOverride)
    {
        string root = FindRepository();
        string configuration = typeof(ClangLibraryHostTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string fixture = Path.Combine(root, "tests", "fixtures", "ParserLibraryHost", "bin", configuration, "net9.0", "ParserLibraryHost.dll");
        string directory = Path.Combine(Path.GetTempPath(), "BgcsParserHost", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string output = Path.Combine(directory, "parsed.txt");
            string project = Path.Combine(directory, "fixture.proj");
            File.WriteAllText(project, $"""
                <Project>
                  <UsingTask TaskName="BGCS.Tests.ParserLibraryHost.ParseNativeApiTask" AssemblyFile="{SecurityElement.Escape(fixture)}" />
                  <Target Name="Test">
                    <ParseNativeApiTask outputPath="{SecurityElement.Escape(output)}" />
                  </Target>
                </Project>
                """);
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in new[] { "msbuild", project, "-t:Test", "-nologo", "-m:1", "-nodeReuse:false" })
                start.ArgumentList.Add(argument);
            start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
            if (invalidOverride)
                start.Environment["BGCS_CLANG_RUNTIME_DIR"] = directory;
            else
                start.Environment.Remove("BGCS_CLANG_RUNTIME_DIR");
            using Process process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("The independent parser host did not finish within 60 seconds.\n"
                    + await stdout + await stderr);
            }
            string diagnostics = await stdout + await stderr;
            if (invalidOverride)
            {
                Assert.NotEqual(0, process.ExitCode);
                Assert.Contains("must contain both", diagnostics);
                Assert.Contains(directory, diagnostics);
                Assert.False(File.Exists(output));
            }
            else
            {
                Assert.True(process.ExitCode == 0, diagnostics);
                Assert.Equal("bgcs_library_add", File.ReadAllText(output));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string FindRepository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BindGen-CS.sln")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("The standalone BGCS test checkout is unavailable.");
    }
}
