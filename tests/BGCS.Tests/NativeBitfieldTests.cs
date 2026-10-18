using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeBitfieldTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task NativeRecords_PreserveSignedCrossByteEnumAndFullWidthBits(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-native-bitfields-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "bits.h"), """
                #include <stddef.h>
                enum Mode { MODE_NEGATIVE = -2, MODE_POSITIVE = 3 };
                struct Bits {
                    unsigned char prefix;
                    unsigned int small : 3;
                    signed int negative : 6;
                    unsigned int : 2;
                    enum Mode mode : 3;
                    unsigned int : 0;
                    unsigned long long wide : 64;
                    unsigned int suffix;
                    _Bool visible : 1;
                };
                union Overlay { unsigned long long bits : 64; unsigned char bytes[8]; };
                void fill_bits(struct Bits* value);
                int verify_bits(const struct Bits* value);
                void fill_overlay(union Overlay* value);
                int verify_overlay(const union Overlay* value);
                size_t native_bits_size(void);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "bits.c"), """
                #include "bits.h"
                #include <string.h>
                #if defined(_WIN32)
                #define EXPORT __declspec(dllexport)
                #else
                #define EXPORT __attribute__((visibility("default")))
                #endif
                EXPORT void fill_bits(struct Bits* value) {
                    memset(value, 0xa5, sizeof(*value));
                    value->prefix = 0x5a; value->small = 5; value->negative = -17;
                    value->mode = MODE_NEGATIVE; value->wide = 0xfedcba9876543210ull; value->suffix = 0x12345678; value->visible = 1;
                }
                EXPORT int verify_bits(const struct Bits* value) {
                    return value->prefix == 0x5a && value->small == 2 && value->negative == -31
                        && value->mode == MODE_POSITIVE && value->wide == 0xffffffffffffffffull && value->suffix == 0x12345678 && !value->visible;
                }
                EXPORT void fill_overlay(union Overlay* value) { value->bits = 0x8877665544332211ull; }
                EXPORT int verify_overlay(const union Overlay* value) { return value->bits == 0xffffffffffffffffull; }
                EXPORT size_t native_bits_size(void) { return sizeof(struct Bits); }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "bits", nativeName = "bitfield_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["bits.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.NativeBits", name = "BitsApi", importMode = mode },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                 cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(result.outputRoot));
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(target)));
            await InvokeAsync(Path.Combine(target, "managed"), binary, mode, sourceRuntime, root);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task InvokeAsync(string sources, string binary, BindingImportMode mode, bool sourceRuntime, string root)
    {
        string factory = mode == BindingImportMode.FunctionTable ? "BitsApi.Load(binary)" : "BitsApi.Create()";
        string probe = $$"""
            using System;
            using System.Runtime.InteropServices;
            namespace Generated.NativeBits;
            public static unsafe class BitfieldProbe {
                public static void Run(string binary) {
                    using var api = {{factory}};
                    Bits* value = (Bits*)NativeMemory.AlignedAlloc((nuint)sizeof(Bits), 8);
                    Overlay* overlay = (Overlay*)NativeMemory.AlignedAlloc((nuint)sizeof(Overlay), 8);
                    try {
                        if (api.Raw.NativeBitsSize() != (nuint)sizeof(Bits)) throw new Exception("Native record size differs.");
                        api.Raw.FillBits(value);
                        if (value->small != 5 || value->negative != -17 || value->mode != Mode.MODE_NEGATIVE || value->wide != 0xfedcba9876543210UL || !value->visible)
                            throw new Exception("Native bit reads or sign extension differ.");
                        byte* before = stackalloc byte[sizeof(Bits)];
                        for (int i=0; i<sizeof(Bits); i++) before[i] = ((byte*)value)[i];
                        uint small = value->small; int negative = value->negative; Mode mode = value->mode; ulong wide = value->wide;
                        value->small = small; value->negative = negative; value->mode = mode; value->wide = wide; value->visible = true;
                        for (int i=0; i<sizeof(Bits); i++) if (before[i] != ((byte*)value)[i]) throw new Exception("Bit setters changed adjacent bits or padding.");
                        value->small = 2; value->negative = -31; value->mode = Mode.MODE_POSITIVE; value->wide = ulong.MaxValue; value->visible = false;
                        if (api.Raw.VerifyBits(value) != 1) throw new Exception("Native code observed incorrect managed bit writes.");
                        api.Raw.FillOverlay(overlay);
                        if (overlay->bits != 0x8877665544332211UL || overlay->bytes[0] != 0x11 || overlay->bytes[7] != 0x88)
                            throw new Exception("Union overlay did not share native byte storage.");
                        overlay->bits = ulong.MaxValue;
                        if (api.Raw.VerifyOverlay(overlay) != 1) throw new Exception("Native union bit write failed.");
                    } finally { NativeMemory.AlignedFree(value); NativeMemory.AlignedFree(overlay); }
                }
            }
            """;
        string consumer = Path.Combine(root, "Consumer");
        Directory.CreateDirectory(consumer);
        await File.WriteAllTextAsync(Path.Combine(consumer, "BitfieldProbe.cs"), probe);
        string runtime = sourceRuntime ? "" : "<Reference Include=\"BGCS.Runtime\"><HintPath>" + SecurityElement.Escape(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location) + "</HintPath></Reference>";
        await File.WriteAllTextAsync(Path.Combine(consumer, "Consumer.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
            <ItemGroup><Compile Include="{{SecurityElement.Escape(Path.Combine(sources, "**", "*.cs"))}}" />{{runtime}}</ItemGroup></Project>
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
        AssemblyLoadContext load = new("BGCS.NativeBits.Test", isCollectible: true);
        nint native = NativeLibrary.Load(binary);
        try
        {
            Assembly assembly = load.LoadFromStream(image);
            NativeLibrary.SetDllImportResolver(assembly, (name, _, _) => name == "bitfield_fixture" ? native : 0);
            assembly.GetType("Generated.NativeBits.BitfieldProbe", true)!.GetMethod("Run")!.Invoke(null, [binary]);
        }
        finally { load.Unload(); NativeLibrary.Free(native); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
