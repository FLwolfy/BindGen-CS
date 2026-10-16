using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NeutralNativeBuildTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Twice(int value);

    [Fact]
    public async Task CBuild_UsesC17AndPreservesPreviousOutputWhenCompilationOrExportsFail()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs native 验收 & " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        nint module = 0;
        try
        {
            string header = Path.Combine(temporary, "native.h");
            string source = Path.Combine(temporary, "native source & test.c");
            await File.WriteAllTextAsync(header, "int twice(int value);");
            string valid = """
                #if defined(_WIN32)
                #define EXPORT __declspec(dllexport)
                #else
                #define EXPORT __attribute__((visibility("default")))
                #endif
                _Static_assert(sizeof(int) == 4, "C17 native fixture");
                EXPORT int twice(int value) { return value * 2; }
                """;
            await File.WriteAllTextAsync(source, valid);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "native", nativeName = "native_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["native.h"], parseMacros = false },
                runtime = new() { mode = "source" }, cache = new() { enabled = false },
                nativeBuild = new() { mode = "sources", sources = [Path.GetFileName(source)] }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult first = await host.BuildAsync(new(new(configuration, temporary)));
            Assert.True(first.success, first.GetDiagnosticSummary());
            string targetRoot = Assert.Single(Directory.GetDirectories(first.outputRoot));
            string binary = Assert.Single(Directory.GetFiles(Path.Combine(targetRoot, "native", "bin")));
            module = NativeLibrary.Load(binary);
            Twice call = Marshal.GetDelegateForFunctionPointer<Twice>(NativeLibrary.GetExport(module, "twice"));
            Assert.Equal(42, call(21));
            NativeLibrary.Free(module);
            module = 0;
            string previous = await File.ReadAllTextAsync(Path.Combine(first.outputRoot, "generation.report.json"));
            byte[] previousBinary = await File.ReadAllBytesAsync(binary);
            await File.WriteAllTextAsync(source, "this is not valid C");
            BuildResult compileFailure = await host.BuildAsync(new(new(configuration, temporary)));
            Assert.False(compileFailure.success);
            Assert.Contains("compile-0", compileFailure.GetDiagnosticSummary());
            Assert.Equal(previous, await File.ReadAllTextAsync(Path.Combine(first.outputRoot, "generation.report.json")));
            Assert.Equal(previousBinary, await File.ReadAllBytesAsync(binary));
            await File.WriteAllTextAsync(source, valid);
            await File.WriteAllTextAsync(header, "struct PackedRecord { char prefix; int value; }; int twice(int value);");
            BindingConfiguration changedAbi = configuration with { source = configuration.source with { arguments = ["-fpack-struct=1"] } };
            BuildResult storageMismatch = await host.BuildAsync(new(new(changedAbi, temporary)));
            Assert.False(storageMismatch.success);
            Assert.Contains("native storage size differs from parsed facts", storageMismatch.GetDiagnosticSummary());
            Assert.Equal(previousBinary, await File.ReadAllBytesAsync(binary));
            await File.WriteAllTextAsync(header, "int twice(int value); int missing(void);");
            BuildResult missing = await host.BuildAsync(new(new(configuration, temporary)));
            Assert.False(missing.success);
            Assert.Contains("missing required exports: missing", missing.GetDiagnosticSummary());
            Assert.Equal(previousBinary, await File.ReadAllBytesAsync(binary));
            await File.WriteAllTextAsync(header, "int twice(int value);");
            string external = Path.Combine(temporary, Path.GetFileName(binary));
            File.Copy(binary, external);
            BindingConfiguration prebuilt = configuration with { nativeBuild = new() { mode = "prebuilt", artifacts = [external], prebuiltTargetId = "linux-x64-musl" } };
            BuildResult wrongTarget = await host.BuildAsync(new(new(prebuilt, temporary)));
            Assert.False(wrongTarget.success);
            Assert.Contains("same concrete native target", wrongTarget.GetDiagnosticSummary());
            prebuilt = prebuilt with { nativeBuild = prebuilt.nativeBuild with { prebuiltTargetId = Path.GetFileName(targetRoot) } };
            BuildResult accepted = await host.BuildAsync(new(new(prebuilt, temporary)));
            Assert.True(accepted.success, accepted.GetDiagnosticSummary());
            using JsonDocument receipt = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(targetRoot, "metadata", "verification.json")));
            Assert.False(receipt.RootElement.GetProperty("nativeLinked").GetBoolean());
            Assert.True(receipt.RootElement.GetProperty("nativeArtifactVerified").GetBoolean());
            Assert.Equal("userAsserted", receipt.RootElement.GetProperty("nativeEvidence").GetProperty("targetIdentityEvidence").GetString());
            Assert.Empty(Directory.GetDirectories(temporary, ".bgcs-staging-*"));
        }
        finally
        {
            if (module != 0) NativeLibrary.Free(module);
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
