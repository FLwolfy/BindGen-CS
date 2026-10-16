using System;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.NativeBuild.Execution;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class StaticExportTableTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Metadata();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Identity();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Address(uint index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeCall(int value);

    [Fact]
    public async Task StaticAdapterValidatesIdentityAndResolvesRealNativeFunctionAddresses()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-static-adapter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        nint library = 0;
        try
        {
            var target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.C);
            InteropModule module = Module(target.targetId.value, IntPtr.Size);
            StaticExportTableSource emitted = StaticExportTableEmitter.Emit(module, ["native.h"]);
            await File.WriteAllTextAsync(Path.Combine(temporary, "native.h"), "int twice(int value);\nint increment(int value);\n");
            string native = Path.Combine(temporary, "native.c"), adapter = Path.Combine(temporary, "adapter.c");
            await File.WriteAllTextAsync(native, "int twice(int value) { return value * 2; }\nint increment(int value) { return value + 1; }\n");
            await File.WriteAllTextAsync(adapter, emitted.source);
            string scratch = Path.Combine(temporary, "scratch");
            Directory.CreateDirectory(scratch);
            string output = Path.Combine(scratch, OperatingSystem.IsWindows() ? "fixture.dll" : OperatingSystem.IsMacOS() ? "libfixture.dylib" : "libfixture.so");
            NativeBuildPlan plan = new NativeBuildProviderResolver().Resolve(target, "auto").Plan(new(target,
                [new(native, SourceLanguage.C, "c17"), new(adapter, SourceLanguage.C, "c17")], scratch, output) { includeDirectories = [temporary] }, CancellationToken.None);
            await new PlannedNativeBuildExecutor().ExecuteAsync(plan, TimeSpan.FromMinutes(2));
            library = NativeLibrary.Load(output);
            T Function<T>(string symbol) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, symbol));
            Assert.Equal(1u, Function<Metadata>(emitted.descriptor.getRevisionSymbol)());
            Assert.Equal(2u, Function<Metadata>(emitted.descriptor.getCountSymbol)());
            Assert.Equal((uint)IntPtr.Size, Function<Metadata>(emitted.descriptor.getPointerSizeSymbol)());
            Assert.Equal(emitted.descriptor.abiIdentity, Marshal.PtrToStringUTF8(Function<Identity>(emitted.descriptor.getAbiIdentitySymbol)()));
            Address address = Function<Address>(emitted.descriptor.getExportSymbol);
            Assert.Equal(42, Marshal.GetDelegateForFunctionPointer<NativeCall>(address(0))(21));
            Assert.Equal(42, Marshal.GetDelegateForFunctionPointer<NativeCall>(address(1))(41));
            Assert.Equal(0, address(2));
            Assert.Equal(0, address(uint.MaxValue));
        }
        finally { if (library != 0) NativeLibrary.Free(library); Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void IdentityAndSymbolsAreDeterministicAndRejectUnprovenCallableForms()
    {
        InteropModule module = Module("linux-x64-gnu", 8);
        StaticExportTableSource first = StaticExportTableEmitter.Emit(module, ["native.h", "native.h"]);
        StaticExportTableSource again = StaticExportTableEmitter.Emit(module, ["native.h"]);
        Assert.Equal(first.source, again.source);
        Assert.Equal(first.descriptor.abiIdentity, again.descriptor.abiIdentity);
        Assert.Equal(new[] { "twice", "increment" }, first.descriptor.exportNames);
        Assert.DoesNotContain('\r', first.source);
        InteropModule different = Module("linux-arm64-gnu", 8);
        Assert.NotEqual(first.descriptor.abiIdentity, StaticExportTableEmitter.Emit(different, ["native.h"]).descriptor.abiIdentity);
        InteropModule variadic = new(module.libraryId, module.targetId, module.pointerSize, module.types, [module.functions[0] with { isVariadic = true }], []);
        Assert.Throws<NotSupportedException>(() => StaticExportTableEmitter.Emit(variadic, ["native.h"]));
        Assert.Throws<ArgumentException>(() => StaticExportTableEmitter.Emit(module, ["bad\"header.h"]));
    }
    private static InteropModule Module(string target, int pointers)
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        return new("static_adapter", target, pointers, [integer], [new(new("twice"), "twice", integer.id, [new(new("twice:arg"), "value", integer.id)], "Cdecl"),
            new(new("increment"), "increment", integer.id, [new(new("increment:arg"), "value", integer.id)], "Cdecl")], []);
    }
}
