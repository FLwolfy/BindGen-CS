using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Requests;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeManagedEnumTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task GeneratedEnumsPreserveFullWidthNativeRoundTripsAndSeparateRawCarriers(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs managed enum " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "enums.h"), """
                #include <stdint.h>
                typedef uint64_t native_flags;
                native_flags echo_flags(native_flags value);
                int64_t echo_signed(int64_t value);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "enums.c"), """
                #include "enums.h"
                #if defined(_WIN32)
                #define EXPORT __declspec(dllexport)
                #else
                #define EXPORT __attribute__((visibility("default")))
                #endif
                EXPORT native_flags echo_flags(native_flags value) { return value; }
                EXPORT int64_t echo_signed(int64_t value) { return value; }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "enum_fixture", nativeName = "enum_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["enums.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new()
                {
                    @namespace = "Generated.EnumFixture", name = "EnumApi", importMode = mode,
                    enums = [new("WideFlags", ManagedIntegralKind.UInt64,
                        [new("Low", "1"), new("High", "9223372036854775808"), new("All", "18446744073709551615")]) { isFlags = true },
                        new("SignedValues", ManagedIntegralKind.Int64, [new("Minimum", "-9223372036854775808"), new("Maximum", "9223372036854775807")])],
                    enumMappings = ImmutableDictionary<string, string>.Empty.Add("native_flags", "WideFlags").Add("int64_t", "SignedValues")
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime && mode == BindingImportMode.LibraryImport ? "singleFile" : "split" },
                nativeBuild = new() { mode = "sources", sources = ["enums.c"] }, cache = new() { enabled = false }
            };
            // Exercise the strict public JSON entry instead of bypassing grouped configuration loading.
            string path = Path.Combine(root, "bindgen.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(configuration, new JsonSerializerOptions
            { WriteIndented = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) } }));
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult result = await host.BuildAsync(BuildRequest.FromFile(path));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(result.outputRoot));
            await InvokeAsync(Path.Combine(target, "managed"), Assert.Single(Directory.GetFiles(Path.Combine(target, "native", "bin"))), mode, sourceRuntime, root);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task InvokeAsync(string sources, string binary, BindingImportMode mode, bool sourceRuntime, string root)
    {
        string factory = mode == BindingImportMode.FunctionTable ? "EnumApi.Load(binary)" : "EnumApi.Create()";
        string consumer = Path.Combine(root, "Consumer");
        Directory.CreateDirectory(consumer);
        await File.WriteAllTextAsync(Path.Combine(consumer, "Probe.cs"), $$"""
            using Generated.EnumFixture;
            public static class EnumProbe {
                public static int Run(string binary) {
                    using var api = {{factory}};
                    if (System.Convert.ToUInt64(WideFlags.High) != 0x8000000000000000UL || System.Convert.ToUInt64(WideFlags.All) != ulong.MaxValue) return 1;
                    if (api.EchoFlags(WideFlags.High | WideFlags.Low) != (WideFlags.High | WideFlags.Low)) return 2;
                    if (api.EchoFlags(WideFlags.All) != WideFlags.All || api.Raw.EchoFlags(ulong.MaxValue) != ulong.MaxValue) return 3;
                    if (api.EchoSigned(SignedValues.Minimum) != SignedValues.Minimum || api.EchoSigned(SignedValues.Maximum) != SignedValues.Maximum) return 4;
                    if (api.Raw.EchoSigned(long.MinValue) != long.MinValue || (long)SignedValues.Minimum != long.MinValue) return 5;
                    return 42;
                }
            }
            """);
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
        AssemblyLoadContext load = new("BGCS.NativeManagedEnum.Test", isCollectible: true);
        nint native = NativeLibrary.Load(binary);
        try
        {
            Assembly assembly = load.LoadFromStream(image);
            NativeLibrary.SetDllImportResolver(assembly, (name, _, _) => name == "enum_fixture" ? native : 0);
            Type flags = assembly.GetType("Generated.EnumFixture.WideFlags", true)!;
            Assert.Equal(typeof(ulong), Enum.GetUnderlyingType(flags));
            Assert.True(flags.IsDefined(typeof(FlagsAttribute), false));
            Assert.Equal(42, assembly.GetType("EnumProbe", true)!.GetMethod("Run")!.Invoke(null, [binary]));
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
