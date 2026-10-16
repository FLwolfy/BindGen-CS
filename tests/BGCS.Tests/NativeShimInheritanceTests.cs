using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeShimInheritanceTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Add(int left, int right, out int result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateInteger(int value, out nint result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Create(out nint result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Cast(nint self, out nint result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Read(nint self, out int result, out nint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Destroy(nint self, out nint error);

    [Fact]
    public async Task ProjectOwnedCShim_UsesDeclaredHeadersSourcesAndExactCLinkage()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-shim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        nint library = 0;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "fixture.hpp"), "#pragma once\nnamespace Demo { inline int Sum(int left, int right) { return left + right; } }\n");
            await File.WriteAllTextAsync(Path.Combine(root, "shim.h"), "#pragma once\nextern \"C\" int demo_sum(int left, int right);\n");
            await File.WriteAllTextAsync(Path.Combine(root, "shim.cpp"), "#include \"fixture.hpp\"\n#include \"shim.h\"\nextern \"C\" int demo_sum(int left, int right) { return Demo::Sum(left, right); }\n");
            BindingConfiguration configuration = Configuration("shim") with
            {
                cpp = new() { nativeShims = [new() { id = "sum", publicHeaders = ["shim.h"], sourceFiles = ["shim.cpp"], exports = ["demo_sum"] }] }
            };
            (CppBridgeModule plan, string binary) = await Build(configuration, root);
            Assert.Contains(plan.headers, path => Path.GetFileName(path) == "shim.h");
            BridgeExport shim = plan.exports.Single(export => export.operation.declaration is { } id && plan.source.GetDeclaration(id).name == "demo_sum");
            Assert.Equal("C", plan.source.GetDeclaration(shim.operation.declaration!.Value).linkage);
            library = NativeLibrary.Load(binary);
            Add invoke = Marshal.GetDelegateForFunctionPointer<Add>(NativeLibrary.GetExport(library, shim.name));
            Assert.Equal(0, invoke(20, 22, out int value, out nint error));
            Assert.Equal(42, value);
            Assert.Equal(0, error);
        }
        finally
        {
            if (library != 0) NativeLibrary.Free(library);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MultipleInheritance_UsesNativePointerAdjustmentAndCheckedDowncast()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-neutral-casts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        nint library = 0;
        CppBridgeModule? plan = null;
        List<(BridgeExport Constructor, nint Instance)> owned = [];
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "fixture.hpp"), """
                #pragma once
                namespace Demo {
                class Left { public: explicit Left(int value) : m_value(value) {} virtual ~Left() noexcept = default; int m_value; };
                class Right { public: explicit Right(int value) : m_value(value) {} virtual ~Right() noexcept = default; int m_value; };
                class Widget : public Left, public Right {
                public: explicit Widget(int value) : Left(value), Right(value + 1) {} int Read() const { return Left::m_value + Right::m_value; }
                };
                class Another : public Left { public: Another() : Left(9) {} };
                }
                """);
            string binary;
            (plan, binary) = await Build(Configuration("casts"), root);
            library = NativeLibrary.Load(binary);
            T Function<T>(BridgeExport export) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, export.name));
            BridgeExport Constructor(string name) => plan.exports.Single(export => export.operation.kind == BridgeOperationKind.Construct
                && plan.source.GetType(export.operation.ownerType!.Value).spelling == "Demo::" + name);
            BridgeExport widget = Constructor("Widget"), another = Constructor("Another");
            Assert.Equal(0, Function<CreateInteger>(widget)(20, out nint instance, out nint error));
            Assert.Equal(0, error);
            owned.Add((widget, instance));
            Assert.Equal(0, Function<Create>(another)(out nint other, out error));
            Assert.Equal(0, error);
            owned.Add((another, other));
            BridgeExport Upcast(BridgeExport constructor, string destination) => plan.exports.Single(export => export.operation.kind == BridgeOperationKind.BaseCast
                && export.operation.ownerType == constructor.operation.ownerType && plan.source.GetType(export.operation.castTarget!.Value).spelling == "Demo::" + destination);
            Assert.Equal(0, Function<Cast>(Upcast(widget, "Left"))(instance, out nint left, out error));
            Assert.Equal(0, error);
            Assert.Equal(0, Function<Cast>(Upcast(widget, "Right"))(instance, out nint right, out error));
            Assert.Equal(0, error);
            Assert.NotEqual(left, right);
            Assert.Equal(0, Function<Read>(plan.exports.Single(export => export.operation.declaration is { } id && plan.source.GetDeclaration(id).qualifiedName == "Demo::Widget::Read"))
                (instance, out int value, out error));
            Assert.Equal(41, value);
            Assert.Equal(0, error);
            BridgeExport downcast = plan.exports.Single(export => export.operation.kind == BridgeOperationKind.CheckedDerivedCast
                && export.operation.castTarget == widget.operation.ownerType && plan.source.GetType(export.operation.ownerType!.Value).spelling == "Demo::Left");
            Assert.Equal(0, Function<Cast>(downcast)(left, out nint restored, out error));
            Assert.Equal(instance, restored);
            Assert.Equal(0, error);
            Assert.Equal(0, Function<Cast>(Upcast(another, "Left"))(other, out nint unrelated, out error));
            Assert.Equal(0, error);
            Assert.Equal(0, Function<Cast>(downcast)(unrelated, out restored, out error));
            Assert.Equal(0, restored);
            Assert.Equal(0, error);
        }
        finally
        {
            if (library != 0 && plan is not null)
            {
                foreach ((BridgeExport constructor, nint instance) in owned.AsEnumerable().Reverse())
                {
                    BridgeExport destroy = plan.exports.Single(export => export.id == constructor.resultRelease);
                    Assert.Equal(0, Marshal.GetDelegateForFunctionPointer<Destroy>(NativeLibrary.GetExport(library, destroy.name))(instance, out nint error));
                    Assert.Equal(0, error);
                }
            }
            if (library != 0) NativeLibrary.Free(library);
            Directory.Delete(root, recursive: true);
        }
    }

    private static BindingConfiguration Configuration(string id) => new()
    {
        schemaVersion = 1, library = new() { id = id, nativeName = id + "_bridge" },
        source = new() { language = SourceLanguage.Cpp, headers = ["fixture.hpp"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
        api = new() { @namespace = "Generated." + id, name = "FixtureApi" },
        runtime = new() { mode = "source" }, cache = new() { enabled = false }, nativeBuild = new() { mode = "sources" }
    };

    private static async Task<(CppBridgeModule Plan, string Binary)> Build(BindingConfiguration configuration, string root)
    {
        string worker = Path.Combine(Repository(), "src", "BGCS.Frontend.Clang.Worker", "bin", BuildConfiguration(), "net10.0");
        await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(worker).Build();
        BuildResult result = await host.BuildAsync(new(new(configuration, root)));
        Assert.True(result.success, result.GetDiagnosticSummary());
        string target = Assert.Single(Directory.GetDirectories(Path.Combine(root, "Generated")));
        CppBridgeModule plan = JsonSerializer.Deserialize<CppBridgeModule>(await File.ReadAllTextAsync(Path.Combine(target, "native", "metadata", "bridge.module.json")))!;
        string name = configuration.library.nativeName;
        return (plan, Path.Combine(target, "native", "bin", OperatingSystem.IsWindows() ? name + ".dll" : OperatingSystem.IsMacOS() ? "lib" + name + ".dylib" : "lib" + name + ".so"));
    }

    private static string BuildConfiguration() => AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
    private static string Repository()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "BindGen-CS.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("The repository root is unavailable.");
    }
}
