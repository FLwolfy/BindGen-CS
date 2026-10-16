using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Tests;

internal static class NativeConsumerHarness
{
    internal static object? InvokeFactory(this MethodInfo method, object?[] arguments)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (arguments.Length > parameters.Length) throw new ArgumentException("Too many explicit factory arguments.", nameof(arguments));
        object?[] supplied = new object?[parameters.Length];
        Array.Copy(arguments, supplied, arguments.Length);
        for (int index = arguments.Length; index < parameters.Length; ++index)
        {
            if (!parameters[index].IsOptional) throw new ArgumentException("A required factory argument was omitted.", nameof(arguments));
            supplied[index] = Type.Missing;
        }
        return method.Invoke(null, supplied);
    }

    internal static async Task<object?> CompileAndInvokeAsync(string managedRoot, string nativeBinary, string nativeLibraryName,
        string probeSource, string probeType, string root, bool sourceRuntime)
    {
        string consumer = Path.Combine(root, "Consumer");
        Directory.CreateDirectory(consumer);
        await File.WriteAllTextAsync(Path.Combine(consumer, "Probe.cs"), probeSource);
        string reference = sourceRuntime ? "" : "<Reference Include=\"BGCS.Runtime\"><HintPath>" + SecurityElement.Escape(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location) + "</HintPath></Reference>";
        await File.WriteAllTextAsync(Path.Combine(consumer, "Consumer.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
            <ItemGroup><Compile Include="{{SecurityElement.Escape(Path.Combine(managedRoot, "**", "*.cs"))}}" />{{reference}}</ItemGroup></Project>
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
        AssemblyLoadContext load = new("BGCS.NativeConsumer." + Guid.NewGuid().ToString("N"), isCollectible: true);
        nint native = NativeLibrary.Load(nativeBinary);
        try
        {
            Assembly assembly = load.LoadFromStream(image);
            NativeLibrary.SetDllImportResolver(assembly, (name, _, _) => name == nativeLibraryName ? native : 0);
            return assembly.GetType(probeType, true)!.GetMethod("Run")!.Invoke(null, [nativeBinary]);
        }
        finally { load.Unload(); NativeLibrary.Free(native); }
    }
}
