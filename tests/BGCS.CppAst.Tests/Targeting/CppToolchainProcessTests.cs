using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;

namespace BGCS.CppAst.Tests;

/// <summary>
/// Verifies compiler discovery against real processes with blocked output and timeout behavior.
/// </summary>
public sealed class CppToolchainProcessTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-query-" + Guid.NewGuid().ToString("N"));
    private readonly string m_compiler;
    private readonly string m_processFile;
    private readonly string m_sdk;

    /// <summary>
    /// Builds an isolated compiler-query fixture using the host C compiler.
    /// </summary>
    public CppToolchainProcessTests()
    {
        Directory.CreateDirectory(m_root);
        m_processFile = Path.Combine(m_root, "query.pid");
        m_sdk = Path.Combine(m_root, "sdk");
        Directory.CreateDirectory(m_sdk);
        m_compiler = BuildCompiler();
    }

    /// <summary>
    /// Verifies that diagnostic output cannot prevent a resource query from completing.
    /// </summary>
    /// <returns>
    /// Completion after the public discovery API returns and its fixture process is retired.
    /// </returns>
    [Fact]
    public async Task CompilerResourceQueryDrainsBothOutputStreams()
    {
        Task<System.Collections.Generic.IReadOnlyList<string>> query = Task.Run(() =>
            CppToolchainDiscovery.DiscoverSystemIncludeFolders(CppParserKind.C, m_compiler));
        try
        {
            var directories = await query.WaitAsync(TimeSpan.FromSeconds(15));

            Assert.Equal([m_sdk], directories);
        }
        finally
        {
            TerminateRecordedProcess();
            await query.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    /// <summary>
    /// Verifies that an unavailable version query leaves no running compiler process.
    /// </summary>
    /// <returns>
    /// Completion after the timed-out query and its owned process have finished.
    /// </returns>
    [Fact]
    public void CompilerFingerprintObservesAChangedDriverAtTheSamePath()
    {
        BuildCompiler(versionTimesOut: false);
        string first = CppToolchainDiscovery.GetCompilerFingerprint(CppParserKind.C, m_compiler);
        Assert.Equal(first, CppToolchainDiscovery.GetCompilerFingerprint(CppParserKind.C, m_compiler));
        DateTime modified = File.GetLastWriteTimeUtc(m_compiler);
        File.SetLastWriteTimeUtc(m_compiler, modified.AddSeconds(1));
        string changed = CppToolchainDiscovery.GetCompilerFingerprint(CppParserKind.C, m_compiler);
        Assert.NotEqual(first, changed);
        Assert.Contains("fixture compiler", changed, StringComparison.Ordinal);
    }

    [Fact]
    public void CompilerIncludeDiscoveryPassesTheSelectedSdkAndSeparatesItsCacheEntry()
    {
        string firstSdk = Path.Combine(m_root, "first-sdk");
        string secondSdk = Path.Combine(m_root, "second-sdk");
        Directory.CreateDirectory(firstSdk);
        Directory.CreateDirectory(secondSdk);

        Assert.Equal([firstSdk], CppToolchainDiscovery.DiscoverSystemIncludeFolders(CppParserKind.C, m_compiler, firstSdk));
        Assert.Equal([secondSdk], CppToolchainDiscovery.DiscoverSystemIncludeFolders(CppParserKind.C, m_compiler, secondSdk));
        Assert.Equal([m_sdk], CppToolchainDiscovery.DiscoverSystemIncludeFolders(CppParserKind.C, m_compiler));
    }

    [Fact]
    public async Task TimedOutCompilerQueryRetiresItsProcessBeforeReturning()
    {
        Task<string> query = Task.Run(() => CppToolchainDiscovery.GetCompilerFingerprint(CppParserKind.C, m_compiler));
        try
        {
            string fingerprint = await query.WaitAsync(TimeSpan.FromSeconds(15));

            Assert.Contains("version:unavailable", fingerprint, StringComparison.Ordinal);
            int processId = int.Parse(File.ReadAllText(m_processFile), CultureInfo.InvariantCulture);
            try
            {
                using Process process = Process.GetProcessById(processId);
                Assert.True(process.HasExited);
            }
            catch (ArgumentException)
            {
                // An exited process may already have been removed from the OS process table.
            }
        }
        finally
        {
            TerminateRecordedProcess();
            await query.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    /// <summary>
    /// Retires any remaining fixture process and removes the isolated test directory.
    /// </summary>
    public void Dispose()
    {
        TerminateRecordedProcess();
        Directory.Delete(m_root, recursive: true);
    }

    private string BuildCompiler(bool versionTimesOut = true)
    {
        string driver = CppToolchainDiscovery.FindCompiler(CppParserKind.C)
            ?? throw new InvalidOperationException("A C compiler is required for process-boundary tests.");
        string executable = Path.Combine(m_root, OperatingSystem.IsWindows() ? "query.exe" : "query");
        string source = Path.Combine(m_root, "query.c");
        string processFile = QuoteCString(m_processFile);
        string sdk = QuoteCString(m_sdk);
        string resource = QuoteCString(Path.Combine(m_root, "resource"));
        string versionAction = versionTimesOut ? "WAIT_FOR_TIMEOUT;" : "puts(\"fixture compiler\");";
        File.WriteAllText(source, $$"""
            #include <stdio.h>
            #include <string.h>
            #ifdef _WIN32
            #include <windows.h>
            #define PROCESS_ID GetCurrentProcessId()
            #define WAIT_FOR_TIMEOUT Sleep(30000)
            #else
            #include <unistd.h>
            #define PROCESS_ID getpid()
            #define WAIT_FOR_TIMEOUT sleep(30)
            #endif
            int main(int argc, char** argv) {
                FILE* pid = fopen({{processFile}}, "w");
                if (!pid) return 1;
                fprintf(pid, "%d", (int)PROCESS_ID);
                fclose(pid);
                if (argc > 1 && strcmp(argv[1], "--version") == 0) {
                    {{versionAction}}
                    return 0;
                }
                if (argc > 1 && strcmp(argv[1], "-print-resource-dir") == 0) {
                    for (int i = 0; i < 20000; i++) fputs("compiler diagnostic output\n", stderr);
                    puts({{resource}});
                    return 0;
                }
                const char* sdk = {{sdk}};
                for (int i = 1; i < argc; i++) {
                    if (strncmp(argv[i], "--sysroot=", 10) == 0) sdk = argv[i] + 10;
                }
                fputs("#include <...> search starts here:\n", stderr);
                fputs(sdk, stderr);
                fputs("\nEnd of search list.\n", stderr);
                return 0;
            }
            """);
        var start = new ProcessStartInfo(driver)
        {
            WorkingDirectory = m_root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in new[] { source, "-o", executable })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        Task.WaitAll(standardOutput, standardError);
        Assert.True(process.ExitCode == 0, standardOutput.Result + standardError.Result);
        return executable;
    }

    private void TerminateRecordedProcess()
    {
        if (!File.Exists(m_processFile))
            return;
        int processId = int.Parse(File.ReadAllText(m_processFile), CultureInfo.InvariantCulture);
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (!process.HasExited && string.Equals(
                    process.MainModule?.FileName,
                    m_compiler,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        catch (ArgumentException)
        {
            // The recorded query has already exited.
        }
    }

    private static string QuoteCString(string value)
        => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
