using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeIrSourceBoundaryTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task ConditionalImportCallbackAndUnknownTextPreserveTheirExactPublicSourceFacts(BindingImportMode mode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-ir-source-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), """
                #if defined(_WIN32)
                #define NATIVE_IMPORT __declspec(dllimport)
                #define NATIVE_CALL __cdecl
                #else
                #define NATIVE_IMPORT
                #define NATIVE_CALL
                #endif
                typedef int (NATIVE_CALL *NativeHandler)(int value);
                NATIVE_IMPORT int NATIVE_CALL NativeInvoke(NativeHandler callback, int value);
                NATIVE_IMPORT const char* NATIVE_CALL GetUnprovenName(int id);
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "ir-source-boundary", nativeName = "ir_source_boundary" },
                source = new() { language = SourceLanguage.C, headers = ["native.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
                api = new() { @namespace = "Generated.IrSource", name = "SourceApi", importMode = mode },
                semantics = new() { callbackParameters = [new() { function = "NativeInvoke", parameter = "callback", lifetime = NativeLifetimeKind.Call,
                    retained = false, threading = NativeCallbackThreading.CallerThread, failureReturn = "-1" }] },
                 cache = new() { enabled = false }
            };
            await using BindingHost host = Host();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root), compileManaged: true);
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.targetModels);
            NativeDeclaration declaration = Assert.Single(target.native.declarations, declaration => declaration.name == "NativeInvoke");
            if (OperatingSystem.IsWindows()) Assert.Contains(declaration.attributes, attribute => attribute.Contains("dllimport", StringComparison.OrdinalIgnoreCase));
            Assert.Single(target.native.types, type => type.category == NativeTypeCategory.Alias && type.spelling == "NativeHandler");
            CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(target.managed!, new("ir_source_boundary", new UnusedSink()) { importMode = mode });
            CSharpAbiDelegatePlan abi = Assert.Single(plan.abiDelegates);
            CSharpCallbackMethodPlan callback = Assert.Single(plan.callScopedCallbacks);
            Assert.Equal("NativeInvoke", plan.methods[callback.methodIndex].exportName);
            Assert.EndsWith("." + abi.name, Assert.Single(callback.callbacks).abiDelegateType, StringComparison.Ordinal);
            InteropFunction unproven = Assert.Single(target.interop.functions, function => function.exportName == "GetUnprovenName");
            Assert.Null(unproven.returnString);
            Assert.Null(unproven.returnOwnership);
            Assert.DoesNotContain(plan.stringResults, text => plan.methods[text.methodIndex].nativeExport == unproven.id);
            MethodDeclarationSyntax[] names = Directory.EnumerateFiles(Path.Combine(result.outputRoot, target.targetId, "managed"), "*.cs", SearchOption.AllDirectories)
                .SelectMany(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                .Where(method => method.Identifier.ValueText == "GetUnprovenName").ToArray();
            MethodDeclarationSyntax raw = Assert.Single(names);
            Assert.Equal("RawApi", raw.Ancestors().OfType<ClassDeclarationSyntax>().First().Identifier.ValueText);
            Assert.IsType<PointerTypeSyntax>(raw.ReturnType);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static BindingHost Host()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0")).Build();
    }

    private sealed class UnusedSink : IArtifactSink
    {
        public string WriteText(string relativePath, string content) => throw new InvalidOperationException("Planning must not emit an artifact.");
    }
}
