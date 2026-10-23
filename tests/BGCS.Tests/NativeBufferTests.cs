using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeBufferTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task DeclaredBuffers_PinExactBoundsPreserveAliasingAndRejectUnsafeCalls(BindingImportMode mode, bool sourceRuntime)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-native-buffer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(temporary, "buffers.h"), """
                #include <stddef.h>
                #if defined(_WIN32)
                #define EXPORT __declspec(dllexport)
                #else
                #define EXPORT __attribute__((visibility("default")))
                #endif
                EXPORT int sum(const int* data, size_t length);
                EXPORT int sum_bytes(const int* data, size_t length);
                EXPORT void increment(int* data, size_t length);
                EXPORT void fill(int* data, size_t length);
                EXPORT void mix(const int* input, int* output, size_t length);
                EXPORT unsigned char small_count(const unsigned char* data, unsigned char length);
                EXPORT size_t inspect_aligned(const int* data, size_t length);
                EXPORT int get_calls(void);
                """);
            await File.WriteAllTextAsync(Path.Combine(temporary, "buffers.c"), """
                #include "buffers.h"
                #include <stdint.h>
                static int calls;
                EXPORT int sum(const int* data, size_t length) { ++calls; int total = 0; for (size_t i=0; i<length; ++i) total += data[i]; return total; }
                EXPORT int sum_bytes(const int* data, size_t length) { ++calls; int total = 0; for (size_t i=0; i<length/sizeof(int); ++i) total += data[i]; return total; }
                EXPORT void increment(int* data, size_t length) { ++calls; for (size_t i=0; i<length; ++i) ++data[i]; }
                EXPORT void fill(int* data, size_t length) { ++calls; for (size_t i=0; i<length; ++i) data[i] = (int)i + 10; }
                EXPORT void mix(const int* input, int* output, size_t length) { ++calls; for (size_t i=0; i<length; ++i) output[i] = input[i] + input[0]; }
                EXPORT unsigned char small_count(const unsigned char* data, unsigned char length) { ++calls; return data ? length : 0; }
                EXPORT size_t inspect_aligned(const int* data, size_t length) { ++calls; return (size_t)((uintptr_t)data % 64); }
                EXPORT int get_calls(void) { return calls; }
                """);
            BufferParameterConfiguration Rule(string function, NativeParameterDirection direction, string parameter = "data", NativeLengthUnit unit = NativeLengthUnit.Elements)
                => new() { function = function, parameter = parameter, lengthParameter = "length", direction = direction, lengthUnit = unit,
                    lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = true };
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2, library = new() { id = "buffers", nativeName = "buffer_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["buffers.h"], parseMacros = false },
                api = new() { @namespace = "Generated.Buffers", name = "BufferApi", importMode = mode },
                semantics = new() { bufferParameters = [Rule("sum", NativeParameterDirection.Input), Rule("sum_bytes", NativeParameterDirection.Input, unit: NativeLengthUnit.Bytes),
                    Rule("increment", NativeParameterDirection.InputOutput), Rule("fill", NativeParameterDirection.Output),
                    Rule("mix", NativeParameterDirection.Input, "input"), Rule("mix", NativeParameterDirection.Output, "output"),
                    Rule("small_count", NativeParameterDirection.Input), Rule("inspect_aligned", NativeParameterDirection.Input) with { requiredAlignment = 64, allowsNull = false }] },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },  cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, temporary));
            Assert.True(built.success, built.GetDiagnosticSummary());
            string target = System.Linq.Enumerable.Single(Directory.GetDirectories(built.outputRoot));
            string native = System.Linq.Enumerable.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(target)));
            string consumer = Path.Combine(temporary, "Consumer");
            Directory.CreateDirectory(consumer);
            string runtime = SecurityElement.Escape(typeof(NativeModuleSession).Assembly.Location)!;
            string sources = SecurityElement.Escape(Path.Combine(target, "managed", "**", "*.cs"))!;
            string reference = sourceRuntime ? "" : $"<Reference Include=\"BGCS.Runtime\"><HintPath>{runtime}</HintPath></Reference>";
            await File.WriteAllTextAsync(Path.Combine(consumer, "Consumer.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
                <ItemGroup><Compile Include="{{sources}}" />{{reference}}</ItemGroup></Project>
                """);
            string create = mode == BindingImportMode.FunctionTable ? "BufferApi.Load(args[0])" : "BufferApi.Create()";
            await File.WriteAllTextAsync(Path.Combine(consumer, "Program.cs"), $$"""
                using System;
                using System.Runtime.InteropServices;
                using Generated.Buffers;
                NativeLibrary.SetDllImportResolver(typeof(BufferApi).Assembly, (name, assembly, path) => NativeLibrary.Load(args[0]));
                using var api = {{create}};
                int[] data = [1,2,3];
                if (api.Sum(data) != 6 || api.SumBytes(data) != 6) throw new Exception("Native buffer bounds were incorrect.");
                if (api.Sum(ReadOnlySpan<int>.Empty) != 0) throw new Exception("Allowed empty buffer failed.");
                api.Increment(data);
                if (data[0] != 2 || data[1] != 3 || data[2] != 4) throw new Exception("Native writes did not reach the original buffer.");
                api.Fill(data);
                if (data[0] != 10 || data[1] != 11 || data[2] != 12) throw new Exception("Native output buffer failed.");
                data = [1,2,3];
                api.Mix(data, data);
                if (data[0] != 2 || data[1] != 4 || data[2] != 5) throw new Exception("Buffer aliasing was changed by temporary copies.");
                int calls = api.GetCalls();
                try { api.Mix(data, new int[2]); throw new Exception("Different shared lengths reached native code."); } catch (ArgumentException) {}
                try { api.SmallCount(new byte[256]); throw new Exception("Overflowing byte count reached native code."); } catch (OverflowException) {}
                try { api.InspectAligned(ReadOnlySpan<int>.Empty); throw new Exception("Forbidden null buffer reached native code."); } catch (ArgumentException) {}
                if (api.GetCalls() != calls) throw new Exception("A rejected buffer invoked native code.");
                if (api.SmallCount(new byte[255]) != 255) throw new Exception("Valid narrow count failed.");
                unsafe {
                    void* memory = NativeMemory.AlignedAlloc(128,64);
                    try {
                        var aligned = new Span<int>(memory,4);
                        aligned.Clear();
                        if (api.InspectAligned(aligned) != 0) throw new Exception("An aligned native-backed span failed.");
                        calls = api.GetCalls();
                        try { api.InspectAligned(aligned.Slice(1)); throw new Exception("Misaligned span reached native code."); } catch (ArgumentException) {}
                        if (api.GetCalls() != calls) throw new Exception("Alignment failure invoked native code.");
                    } finally { NativeMemory.AlignedFree(memory); }
                }
                Console.WriteLine("native-buffer-ok");
                """);
            string buildOutput = await RunAsync(consumer, "build", "Consumer.csproj", "-c", "Release", "-v:minimal", "-m:1", "-nr:false", "-p:UseSharedCompilation=false");
            string consumerAssembly = Path.Combine(consumer, "bin", "Release", "net10.0", "Consumer.dll");
            Assert.True(File.Exists(consumerAssembly), "The SDK did not produce the fixture consumer assembly: " + consumerAssembly + Environment.NewLine + buildOutput);
            Assert.Contains("native-buffer-ok", await RunAsync(consumer, consumerAssembly, native));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static async Task<string> RunAsync(string directory, params string[] arguments)
    {
        string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        string executable = string.IsNullOrEmpty(root) ? "dotnet" : Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        ProcessStartInfo start = new(executable) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(deadline.Token); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        string text = await output + Environment.NewLine + await error;
        Assert.True(process.ExitCode == 0, text);
        return text;
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
