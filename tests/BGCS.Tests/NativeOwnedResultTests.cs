using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeOwnedResultTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task GeneratedOwnedResult_KeepsItsReleaseAliveAfterContextDisposal(
        BindingImportMode mode,
        bool sourceRuntime
    ) {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-owned-result-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(temporary, "widget.hpp"), """
                #include <stdexcept>
                class Widget {
                    inline static int s_live = 0;
                    int m_value;
                public:
                    explicit Widget(int value) : m_value(value) { if (value < 0) throw std::runtime_error("negative"); ++s_live; }
                    ~Widget() noexcept { --s_live; }
                    static int Live() { return s_live; }
                    int Add(int delta) { if (delta < 0) throw std::runtime_error("negative delta"); return m_value + delta; }
                    void Set(int value) { m_value = value; }
                };
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "owned", nativeName = "owned_fixture" },
                source = new() { language = SourceLanguage.Cpp, headers = ["widget.hpp"], parseMacros = false },
                api = new()
                {
                    @namespace = "Generated.Owned",
                    name = "WidgetApi",
                    importMode = mode,
                    names = ImmutableDictionary<string, string>.Empty.Add("Widget::Widget", "CreateWidget").Add("Widget::Live", "Live").Add("Widget::Add", "Add").Add("Widget::Set", "Set")
                },
                semantics = new() { ownedResults = [new() { function = "Widget::Widget", releaseThreadDomain = "any" }] },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, temporary));
            Assert.True(built.success, built.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(built.outputRoot));
            string native = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(target)));
            string consumer = Path.Combine(temporary, "Consumer");
            Directory.CreateDirectory(consumer);
            string runtime = SecurityElement.Escape(typeof(NativeModuleSession).Assembly.Location)!;
            string sources = SecurityElement.Escape(Path.Combine(target, "managed", "**", "*.cs"))!;
            string reference = sourceRuntime ? "" : $"<Reference Include=\"BGCS.Runtime\"><HintPath>{runtime}</HintPath></Reference>";
            await File.WriteAllTextAsync(Path.Combine(consumer, "Consumer.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
                  <ItemGroup><Compile Include="{{sources}}" />{{reference}}</ItemGroup>
                </Project>
                """);
            string create = mode == BindingImportMode.FunctionTable ? "WidgetApi.Load(args[0])" : "WidgetApi.Create()";
            string owner = mode == BindingImportMode.FunctionTable ? "WidgetApi.FromProvider(new ObservedProvider(args[0]))" : create;
            string runtimeNamespace = sourceRuntime ? "Generated.Owned.Runtime" : "BGCS.Runtime";
            string observedProvider = mode == BindingImportMode.FunctionTable ? $$"""
                sealed class ObservedProvider : {{runtimeNamespace}}.INativeModuleProvider {
                    private readonly {{runtimeNamespace}}.DynamicModuleProvider m_inner;
                    public static int releases;
                    public ObservedProvider(string path) { m_inner = new(path); }
                    public bool TryGetExport(string name, out nint address) => m_inner.TryGetExport(name, out address);
                    public void Dispose() { ++releases; m_inner.Dispose(); }
                }
                """ : "static class ObservedProvider { public static int releases => 0; }";
            await File.WriteAllTextAsync(Path.Combine(consumer, "Program.cs"), $$"""
                using System;
                using System.Runtime.InteropServices;
                using Generated.Owned;
                NativeLibrary.SetDllImportResolver(typeof(WidgetApi).Assembly, (name, assembly, path) => NativeLibrary.Load(args[0]));
                using var observer = {{create}};
                using var api = {{owner}};
                if (observer.Live() != 0) throw new Exception("Unexpected initial objects.");
                try { using var invalid = api.CreateWidget(-1); throw new Exception("A failing constructor succeeded."); }
                catch ({{(sourceRuntime ? "Generated.Owned.Runtime" : "BGCS.Runtime")}}.NativeCallException) {}
                if (observer.Live() != 0) throw new Exception("A failing constructor leaked.");
                using var owned = api.CreateWidget(42);
                if (owned.Add(2) != 44) throw new Exception("Typed native member dispatch failed.");
                owned.Set(40);
                if (owned.Add(2) != 42) throw new Exception("Typed native void member dispatch failed.");
                try { owned.Add(-1); throw new Exception("A failing member succeeded."); }
                catch ({{(sourceRuntime ? "Generated.Owned.Runtime" : "BGCS.Runtime")}}.NativeCallException) {}
                if (observer.Live() != 1 || !owned.Invoke(address => address != 0)) throw new Exception("Owned result was not usable.");
                var borrowed = owned.AsBorrowed();
                if (!borrowed.Invoke(address => address != 0)) throw new Exception("Borrowed view failed.");
                api.Dispose();
                var closing = api.DisposeAsync();
                if (closing.IsCompleted) throw new Exception("Context unloading completed while an owned object was alive.");
                if (ObservedProvider.releases != 0) throw new Exception("The object's module was unloaded early.");
                try { owned.Invoke(address => address); throw new Exception("Closed context admitted an object call."); }
                catch (ObjectDisposedException) {}
                if (observer.Live() != 1) throw new Exception("Context disposal destroyed the object early.");
                owned.Dispose();
                owned.Dispose();
                closing.AsTask().GetAwaiter().GetResult();
                if (ObservedProvider.releases != {{(mode == BindingImportMode.FunctionTable ? 1 : 0)}}) throw new Exception("Owned module release was not deferred exactly once.");
                if (owned.releaseFailure != null || observer.Live() != 0) throw new Exception("Object cleanup did not execute exactly once.");
                try { borrowed.Invoke(address => address); throw new Exception("Released borrowed view was usable."); }
                catch (ObjectDisposedException) {}
                Console.WriteLine("owned-lifetime-ok");
                {{observedProvider}}
                """);
            string buildOutput = await RunAsync(consumer, "build", "Consumer.csproj", "-c", "Release", "-v:minimal", "-m:1", "-nr:false", "-p:UseSharedCompilation=false");
            string consumerAssembly = Path.Combine(consumer, "bin", "Release", "net10.0", "Consumer.dll");
            Assert.True(File.Exists(consumerAssembly), "The SDK did not produce the fixture consumer assembly: " + consumerAssembly + Environment.NewLine + buildOutput);
            string output = await RunAsync(consumer, consumerAssembly, native);
            Assert.Contains("owned-lifetime-ok", output);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static async Task<string> RunAsync(
        string directory,
        params string[] arguments
    ) {
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
