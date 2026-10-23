using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Requests;
using BGCS.Runtime;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class NeutralImportCompilationTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ActualSdkBuild_ValidatesImportsAndNativeStorageWithWarningsAsErrors(BindingImportMode mode, bool sourceRuntime)
    {
        NeutralBackendTests.MemorySink sink = new();
        var model = NeutralBackendTests.ModelWithStorage();
        var octet = new BGCS.Core.Interop.InteropType(new("text-octet"), "char", BGCS.Core.Native.NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "Char", isSigned = true };
        var pointer = new BGCS.Core.Interop.InteropType(new("text-pointer"), "char*", BGCS.Core.Native.NativeTypeCategory.Pointer, 8, 8) { elementType = octet.id };
        var evidence = new BGCS.Core.Common.Evidence(BGCS.Core.Common.EvidenceLevel.UserAsserted, "test:declared-text-input");
        var contract = new BGCS.Core.Interop.StringContract(BGCS.Core.Interop.NativeTextEncoding.Utf8, 1, BGCS.Core.Interop.NativeLengthUnit.CodeUnits,
            BGCS.Core.Interop.NativeParameterDirection.Input, new(BGCS.Core.Interop.NativeLifetimeKind.Call, null, evidence), evidence)
        { zeroTerminated = true, retainsPointer = false, allowsNull = false };
        var textFunction = new BGCS.Core.Interop.InteropFunction(new("read_text"), "read_text", model.interop.types[0].id,
            [new(new("text-argument"), "text", pointer.id) { text = contract }], "C");
        var textModule = new BGCS.Core.Interop.InteropModule(model.interop.libraryId, model.interop.targetId, model.interop.pointerSize,
            model.interop.types.AddRange(System.Collections.Immutable.ImmutableArray.Create(octet, pointer)), model.interop.functions.Add(textFunction), []) { byteOrder = model.interop.byteOrder };
        var integer = textModule.types[0];
        var valuePointer = new BGCS.Core.Interop.InteropType(new("borrowed-integer-pointer"), "int*", BGCS.Core.Native.NativeTypeCategory.Pointer, 8, 8)
            { elementType = integer.id };
        var lifetime = new BGCS.Core.Interop.LifetimeContract(BGCS.Core.Interop.NativeLifetimeKind.Call, null, evidence);
        BGCS.Core.Interop.InteropParameter Value(string name, BGCS.Core.Interop.NativeParameterDirection direction) => new(new("borrowed-" + name), name, valuePointer.id)
        { singleValue = new(direction, lifetime, evidence) { retainsPointer = false, allowsNull = false } };
        var protocol = new BGCS.Core.Interop.InteropFunction(new("write-protocol"), "write_protocol", integer.id,
            [new(new("protocol-buffer"), "buffer", valuePointer.id)
                { buffer = new(integer.id, BGCS.Core.Interop.NativeLengthUnit.Elements, BGCS.Core.Interop.NativeParameterDirection.Output, lifetime, evidence)
                    { capacityParameter = new("protocol-capacity"), writtenCountParameter = new("protocol-count"), retainsPointer = false, allowsNull = true } },
                new(new("protocol-capacity"), "capacity", integer.id), new(new("protocol-count"), "written", valuePointer.id),
                Value("state", BGCS.Core.Interop.NativeParameterDirection.InputOutput)], "C");
        var aliases = new BGCS.Core.Interop.InteropFunction(new("alias-protocol"), "alias_protocol", integer.id,
            [Value("input", BGCS.Core.Interop.NativeParameterDirection.Input), Value("aliased-state", BGCS.Core.Interop.NativeParameterDirection.InputOutput) with { name = "state" },
                Value("output", BGCS.Core.Interop.NativeParameterDirection.Output)], "C");
        textModule = new(textModule.libraryId, textModule.targetId, textModule.pointerSize, textModule.types.Add(valuePointer),
            textModule.functions.AddRange(System.Collections.Immutable.ImmutableArray.Create(protocol, aliases)), []) { byteOrder = textModule.byteOrder };
        model = new BGCS.CSharp.Projection.DefaultManagedProjection().Project(textModule, new("Generated.Example", "NativeApi"), CancellationToken.None);
        if (sourceRuntime)
        {
            var shadows = new[] { "System", "TaskCompletionSource", "NativeModuleSession" }.Select(name => new BGCS.Core.Interop.InteropType(new(name), name, BGCS.Core.Native.NativeTypeCategory.Record, 4, 4) { declarationName = name });
            var interop = new BGCS.Core.Interop.InteropModule(model.interop.libraryId, model.interop.targetId, model.interop.pointerSize,
                model.interop.types.AddRange(shadows), model.interop.functions, []) { byteOrder = model.interop.byteOrder };
            model = new BGCS.CSharp.Projection.DefaultManagedProjection().Project(interop, new("Generated.Example", "NativeApi"), CancellationToken.None);
        }
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink)
        { importMode = mode, includeRuntimeSource = sourceRuntime, runtimeNamespace = sourceRuntime ? "Generated.Example.Runtime" : "BGCS.Runtime" }, CancellationToken.None);
        sink.WriteText("ReferenceConsumer.cs", """
            public static class ReferenceConsumer {
                public static int Call(Generated.Example.NativeApi api, global::System.Span<int> buffer) {
                    int value=2;
                    int result=api.WriteProtocol(buffer,out int written,ref value);
                    return result+written+api.AliasProtocol(in value,ref value,out value);
                }
            }
            """);
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-neutral-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (var source in sink.sources)
            {
                string path = Path.Combine(temporary, source.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, source.Value);
            }
            string runtime = SecurityElement.Escape(typeof(NativeModuleSession).Assembly.Location)!;
            string reference = sourceRuntime ? "" : "<ItemGroup><Reference Include=\"BGCS.Runtime\"><HintPath>" + runtime + "</HintPath></Reference></ItemGroup>";
            string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>" + reference + "</Project>";
            await File.WriteAllTextAsync(Path.Combine(temporary, "Consumer.csproj"), project);
            string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            string executable = string.IsNullOrEmpty(root) ? "dotnet" : Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            ProcessStartInfo start = new(executable) { WorkingDirectory = temporary, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (string argument in new[] { "build", "Consumer.csproj", "-c", "Release", "-v:minimal", "-m:1", "-nr:false", "-p:UseSharedCompilation=false" }) start.ArgumentList.Add(argument);
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the .NET SDK.");
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(deadline.Token); }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            }
            string diagnostics = await output + Environment.NewLine + await error;
            Assert.True(process.ExitCode == 0, diagnostics);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }
}
