using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Inputs;
using BGCS.Cpp2C.Planning;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeTemplateBindingTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(int input, out int result, out nint error);

    [Theory]
    [InlineData("return")]
    [InlineData("parameter")]
    [InlineData("alias")]
    public async Task FinalTemplateSignatureDrift_CannotHideBehindWrapperConversions(string changed)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-template-binding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "template.hpp"), """
                namespace Demo {
                #ifdef BGCS_TEMPLATE_RETURN_FLOAT
                using Result = float;
                #else
                using Result = int;
                #endif
                #ifdef BGCS_TEMPLATE_PARAMETER_FLOAT
                using Parameter = float;
                #else
                using Parameter = int;
                #endif
                #ifdef BGCS_TEMPLATE_ALIAS_FLOAT
                using Scalar = float;
                #else
                using Scalar = int;
                #endif
                template<class T> Result ReturnValue(T value) { return static_cast<Result>(value + value); }
                template<class T> int ParameterValue(Parameter value) { return static_cast<int>(value + value); }
                template<class T> T AliasValue(T value) { return value + value; }
                }
                """);
            string callable = changed switch { "return" => "Demo::ReturnValue<int>", "parameter" => "Demo::ParameterValue<int>", _ => "Demo::AliasValue<Demo::Scalar>" };
            string scalar = changed == "alias" ? "Demo::Scalar" : "int";
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "template_binding", nativeName = "template_binding" },
                source = new() { language = SourceLanguage.Cpp, headers = ["template.hpp"], parseMacros = false },
                cpp = new() { templateInstantiations = new() { functions = [new("selected", callable, scalar, [scalar])] } },
                api = new() { @namespace = "Generated.TemplateBinding", name = "TemplateBindingApi" },
                nativeBuild = new() { mode = "sources" }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult first = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(first.success, first.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(first.outputRoot));
            CppBridgeModule module = JsonSerializer.Deserialize<CppBridgeModule>(await File.ReadAllTextAsync(Path.Combine(target, "native", "metadata", "bridge.module.json")))!;
            string wrapper = new TemplateInstantiationPlanner().Plan(configuration.cpp.templateInstantiations).functionWrappers["selected"];
            BridgeExport export = Assert.Single(module.exports, value => value.operation.declaration is { } id && module.source.GetDeclaration(id).qualifiedName == wrapper);
            nint binary = NativeLibrary.Load(Assert.Single(Directory.GetFiles(Path.Combine(target, "native", "bin"))));
            try
            {
                Assert.Equal(0, Marshal.GetDelegateForFunctionPointer<Invoke>(NativeLibrary.GetExport(binary, export.name))(21, out int result, out nint error));
                Assert.Equal(42, result);
                Assert.Equal(0, error);
            }
            finally { NativeLibrary.Free(binary); }
            var before = Hashes(first.outputRoot);
            configuration = configuration with { nativeBuild = configuration.nativeBuild with
                { compilerArguments = ["-DBGCS_TEMPLATE_" + changed.ToUpperInvariant() + "_FLOAT=1"] } };
            BuildResult divergent = await host.BuildAsync(new(new(configuration, root)));
            Assert.False(divergent.success);
            Assert.Equal(before, Hashes(first.outputRoot));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string[] Hashes(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
