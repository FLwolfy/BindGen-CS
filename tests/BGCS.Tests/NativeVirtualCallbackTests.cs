using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeVirtualCallbackTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Callback(nint userData, int value, out int result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReferenceCallback(nint userData, nint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateProxy(nint userData, nint callback0, nint callback1, out nint result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Cast(nint self, out nint result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(nint self, int value, out int result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count(out int result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Destroy(nint self, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleaseError(nint error);

    [Fact]
    public async Task RawVirtualProxy_ParsesTwiceInvokesManagedSlotAndContainsFailures()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-virtual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        nint library = 0, instance = 0;
        CppBridgeModule? plan = null;
        BridgeExport? factory = null;
        Callback? callback = null;
        ReferenceCallback? referenceCallback = null;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "fixture.hpp"), """
                namespace Demo {
                inline int live = 0;
                class Calculator {
                public:
                    Calculator() { ++live; }
                    virtual ~Calculator() noexcept { --live; }
                    virtual int Compute(int value) const = 0;
                    virtual void Notify(int& value) volatile = 0;
                };
                inline int Invoke(Calculator* calculator, int value) { return calculator->Compute(value); }
                inline int Notify(Calculator* calculator, int value) { calculator->Notify(value); return value; }
                inline int Live() { return live; }
                }
                """, new UTF8Encoding(false));
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1,
                library = new() { id = "virtual", nativeName = "virtual_bridge" },
                source = new() { language = SourceLanguage.Cpp, headers = ["fixture.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                cpp = new() { virtualCallbacks = [new("calculator", "Demo::Calculator")] },
                api = new() { @namespace = "Generated.Virtual", name = "VirtualApi" },
                runtime = new() { mode = "source" }, cache = new() { enabled = false }, nativeBuild = new() { mode = "sources" }
            };
            string worker = Path.Combine(Repository(), "src", "BGCS.Frontend.Clang.Worker", "bin", BuildConfiguration(), "net10.0");
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(worker).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(Path.Combine(root, "Generated")));
            plan = JsonSerializer.Deserialize<CppBridgeModule>(await File.ReadAllTextAsync(Path.Combine(target, "native", "metadata", "bridge.module.json")))!;
            CppVirtualProxyPlan proxy = Assert.Single(plan.virtualProxies);
            Assert.Equal(2, proxy.slots.Length);
            VirtualCallbackSlot slot = Assert.Single(proxy.slots, candidate => plan.source.GetDeclaration(candidate.nativeMethod).name == "Compute");
            Assert.Equal("int32_t", slot.result!.cType);
            Assert.Equal("int32_t", Assert.Single(slot.parameters).cType);
            VirtualCallbackSlot referenceSlot = Assert.Single(proxy.slots, candidate => plan.source.GetDeclaration(candidate.nativeMethod).name == "Notify");
            Assert.Null(referenceSlot.result);
            Assert.Equal("void*", Assert.Single(referenceSlot.parameters).cType);
            Assert.True(plan.source.GetDeclaration(referenceSlot.nativeMethod).isVolatile);
            Assert.False(plan.source.GetDeclaration(referenceSlot.nativeMethod).isNoThrow);
            InteropModule interop = Assert.Single(result.generation.targetModels).interop;
            new BGCS.Analysis.Bridge.VirtualProxyProtocolVerifier().Verify(interop);
            VirtualProxyContract protocol = Assert.Single(interop.virtualProxies);
            Assert.Equal(proxy.id, protocol.name);
            Assert.Equal(proxy.nativeInterface, protocol.sourceInterface);
            Assert.Equal("Demo::Calculator", protocol.sourceQualifiedName);
            Assert.Equal(NativeLifetimeKind.Unknown, protocol.lifetime.kind);
            Assert.Equal(EvidenceLevel.Unknown, protocol.lifetime.evidence.level);
            Assert.Equal(NativeQuiescenceKind.Unknown, protocol.quiescence);
            VirtualProxySlotContract computeContract = Assert.Single(protocol.slots, value => value.sourceMethod == slot.nativeMethod);
            Assert.Equal("Compute", computeContract.sourceName);
            Assert.Equal(1, Assert.Single(computeContract.parameters).index);
            Assert.Equal("value", computeContract.parameters[0].sourceName);
            Assert.Equal(2, computeContract.result!.index);
            Assert.Equal(NativeTypeCategory.Function, interop.GetType(computeContract.callableType).category);
            VirtualProxySlotContract notifyContract = Assert.Single(protocol.slots, value => value.sourceMethod == referenceSlot.nativeMethod);
            Assert.Equal(NativeTypeCategory.Reference, Assert.Single(notifyContract.parameters).sourceCategory);
            Assert.Null(notifyContract.result);
            using (JsonDocument abiDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(target, "metadata", "abi.manifest.json"))))
            {
                InteropModule restored = abiDocument.RootElement.GetProperty("module").Deserialize<InteropModule>()!;
                new BGCS.Analysis.Bridge.VirtualProxyProtocolVerifier().Verify(restored);
                Assert.Equal(protocol.createFunction, Assert.Single(restored.virtualProxies).createFunction);
            }
            factory = Assert.Single(plan.exports, export => export.operation.declaration is { } id && plan.source.GetDeclaration(id).qualifiedName == proxy.factory);
            Assert.Equal(factory.id, protocol.createFunction);
            Assert.Equal(factory.resultRelease, protocol.destroyFunction);
            Assert.Equal(protocol.destroyFunction, protocol.ownership.releaseFunction);
            AnalysisResult headerAnalysis = await host.AnalyzeAsync(new([Path.Combine(target, "native", "include", plan.exportPrefix + ".h")], SourceLanguage.C, root)
            { parseMacros = false });
            Assert.True(headerAnalysis.success, string.Join(Environment.NewLine, headerAnalysis.diagnostics.Select(diagnostic => diagnostic.message)));
            NativeModule checkedHeader = headerAnalysis.module!;
            string callbackName = plan.types.Single(type => type.id == slot.bridgeCallableType).cName;
            NativeType callbackAlias = checkedHeader.types.Single(type => type.category == NativeTypeCategory.Alias && type.declaration is { } declaration
                && checkedHeader.GetDeclaration(declaration).name == callbackName);
            NativeType exactCallback = BGCS.Analysis.Bridge.StandardLibraryFactReader.Resolve(checkedHeader, callbackAlias.id);
            NativeModule corruptedCallback = new(checkedHeader.targetId, checkedHeader.triple, checkedHeader.pointerSize,
                checkedHeader.types.Select(type => type.id == exactCallback.id ? type with { isVariadic = true } : type).ToImmutableArray(), checkedHeader.declarations, checkedHeader.diagnostics)
            { language = checkedHeader.language, byteOrder = checkedHeader.byteOrder };
            Assert.Throws<InvalidOperationException>(() => new BGCS.Analysis.Bridge.BridgeContractVerifier().VerifyAndMerge(plan, corruptedCallback, "virtual"));
            CppBridgeModule wrongFactorySlot = plan with
            { virtualProxies = [proxy with { slots = proxy.slots.Replace(slot, slot with { bridgeCallableType = new("missing-callback-type") }) }] };
            Assert.Throws<InvalidOperationException>(() => new BGCS.Analysis.Bridge.CppBridgeOperationVerifier().Verify(wrongFactorySlot));
            Assert.DoesNotContain(plan.exports, export => export.operation.declaration is { } id
                && plan.source.GetDeclaration(id).qualifiedName.StartsWith(proxy.proxyType + "::", StringComparison.Ordinal));
            string binary = Path.Combine(target, "native", "bin", OperatingSystem.IsWindows() ? "virtual_bridge.dll" : OperatingSystem.IsMacOS() ? "libvirtual_bridge.dylib" : "libvirtual_bridge.so");
            library = NativeLibrary.Load(binary);
            T Function<T>(BridgeExport export) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, export.name));
            ReleaseError releaseError = Marshal.GetDelegateForFunctionPointer<ReleaseError>(NativeLibrary.GetExport(library, plan.errorReleaseExport));
            Count live = Function<Count>(plan.exports.Single(export => export.operation.declaration is { } id && plan.source.GetDeclaration(id).qualifiedName == "Demo::Live"));
            CreateProxy create = Function<CreateProxy>(factory);
            int calls = 0;
            callback = (nint userData, int value, out int output) =>
            {
                calls++;
                output = value + checked((int)userData);
                return value < 0 ? 73 : 0;
            };
            int referenceCalls = 0;
            referenceCallback = (nint userData, nint value) =>
            {
                referenceCalls++;
                int input = Marshal.ReadInt32(value);
                if (input < 0) return 91;
                Marshal.WriteInt32(value, input + checked((int)userData));
                return 0;
            };
            nint callbackPointer = Marshal.GetFunctionPointerForDelegate(callback);
            nint referencePointer = Marshal.GetFunctionPointerForDelegate(referenceCallback);
            nint[] callbacks = proxy.slots.Select(candidate => candidate.nativeMethod == slot.nativeMethod ? callbackPointer : referencePointer).ToArray();
            Assert.NotEqual(0, create(10, 0, 0, out nint rejected, out nint error));
            Assert.Equal(0, rejected);
            Assert.NotEqual(0, error);
            releaseError(error);
            Assert.Equal(0, live(out int count, out error));
            Assert.Equal(0, count);
            Assert.Equal(0, error);
            Assert.Equal(0, create(10, callbacks[0], callbacks[1], out instance, out error));
            Assert.Equal(0, error);
            BridgeExport upcast = plan.exports.Single(export => export.operation.kind == BridgeOperationKind.BaseCast
                && export.operation.ownerType == factory.nativeResultType && export.operation.castTarget == proxy.nativeInterface);
            Assert.Equal(upcast.id, protocol.interfaceCastFunction);
            Assert.Equal(0, Function<Cast>(upcast)(instance, out nint nativeInterface, out error));
            Assert.Equal(0, error);
            Invoke invoke = Function<Invoke>(plan.exports.Single(export => export.operation.declaration is { } id && plan.source.GetDeclaration(id).qualifiedName == "Demo::Invoke"));
            Assert.Equal(0, invoke(nativeInterface, 32, out int value, out error));
            Assert.Equal(42, value);
            Assert.Equal(0, error);
            Assert.Equal(1, calls);
            Assert.NotEqual(0, invoke(nativeInterface, -1, out value, out error));
            Assert.Equal(0, value);
            Assert.NotEqual(0, error);
            releaseError(error);
            Assert.Equal(2, calls);
            Invoke notify = Function<Invoke>(plan.exports.Single(export => export.operation.declaration is { } id && plan.source.GetDeclaration(id).qualifiedName == "Demo::Notify"));
            Assert.Equal(0, notify(nativeInterface, 32, out value, out error));
            Assert.Equal(42, value);
            Assert.Equal(0, error);
            Assert.Equal(1, referenceCalls);
            Assert.NotEqual(0, notify(nativeInterface, -1, out value, out error));
            Assert.Equal(0, value);
            Assert.NotEqual(0, error);
            releaseError(error);
            Assert.Equal(2, referenceCalls);
            Assert.Equal(0, live(out count, out error));
            Assert.Equal(1, count);
            Assert.Equal(0, error);
            BridgeExport destroy = plan.exports.Single(export => export.id == factory.resultRelease);
            Assert.Equal(0, Function<Destroy>(destroy)(instance, out error));
            instance = 0;
            Assert.Equal(0, error);
            Assert.Equal(0, live(out count, out error));
            Assert.Equal(0, count);
            Assert.Equal(0, error);
        }
        finally
        {
            if (library != 0 && instance != 0 && plan is not null && factory is not null)
            {
                BridgeExport destroy = plan.exports.Single(export => export.id == factory.resultRelease);
                Destroy release = Marshal.GetDelegateForFunctionPointer<Destroy>(NativeLibrary.GetExport(library, destroy.name));
                Assert.Equal(0, release(instance, out nint error));
                Assert.Equal(0, error);
            }
            GC.KeepAlive(callback);
            GC.KeepAlive(referenceCallback);
            if (library != 0) NativeLibrary.Free(library);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string BuildConfiguration() => AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
    private static string Repository()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "BindGen-CS.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("The repository root is unavailable.");
    }
}
