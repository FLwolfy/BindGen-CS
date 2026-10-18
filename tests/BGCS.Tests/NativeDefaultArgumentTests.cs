using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeDefaultArgumentTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task TypedDefaultsShareConversionsWithAliasesViewsAndSharedContexts(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-default-arguments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "defaults.h"), """
                #if defined(_WIN32)
                #define EXPORT __declspec(dllexport)
                #else
                #define EXPORT __attribute__((visibility("default")))
                #endif
                EXPORT int calculate(int value, int offset, const char* label);
                EXPORT unsigned long long identity(unsigned long long value);
                EXPORT int identity_i(int value);
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "defaults.c"), """
                #include "defaults.h"
                EXPORT int calculate(int value, int offset, const char* label) {
                  int length = 0; while (label[length]) ++length; return value + offset + length;
                }
                EXPORT unsigned long long identity(unsigned long long value) { return value; }
                EXPORT int identity_i(int value) { return value; }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "defaults", nativeName = "default_fixture" },
                source = new() { headers = ["defaults.h"], includeTransitivelyReferencedHeaders = false, parseMacros = false },
                api = new()
                {
                    @namespace = "Generated.Defaults", name = "DefaultApi", importMode = mode,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
                    names = ImmutableDictionary<string, string>.Empty.Add("calculate::value", "input"),
                    functionAliases = [new("calculate", "Plus")],
                    functionContainers = [new("Numbers", [new("calculate", "Sum")])],
                    functions =
                    [
                        new("calculate") { name = "Calculate", documentation = "Computes <input> && a Unicode label.",
                            defaults = [new("offset", new(ManagedDefaultValueKind.Integer, "7")), new("label", new(ManagedDefaultValueKind.String, "ABC"))] },
                        new("identity") { defaults = [new("value", new(ManagedDefaultValueKind.Integer, "18446744073709551615"))] },
                        new("identity_i") { defaults = [new("value", new(ManagedDefaultValueKind.ConstantReference, "global::System.Int32.MaxValue"))] }
                    ]
                },
                semantics = new() { stringParameters = [new() { function = "calculate", parameter = "label", encoding = NativeTextEncoding.Utf8,
                    codeUnitWidth = 1, lengthUnit = NativeLengthUnit.CodeUnits, direction = NativeParameterDirection.Input,
                    lifetime = NativeLifetimeKind.Call, zeroTerminated = true, retainsPointer = false, allowsNull = false }] },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                 cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.targetModels);
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(target.managed!, new(configuration.library.nativeName, new NoWrites())
            { importMode = mode, runtimeNamespace = sourceRuntime ? "Generated.Defaults.Runtime" : "BGCS.Runtime", includeRuntimeSource = sourceRuntime });
            Assert.Equal(3, plan.methods.Length);
            Assert.Equal(8, plan.defaultOverloads.Length);
            Assert.Single(plan.defaultConstants);
            Assert.Equal(4, Assert.Single(plan.functionContainers).methods.Length);
            var sources = Directory.GetFiles(Path.Combine(targetRoot, "managed"), "*.cs", SearchOption.AllDirectories)
                .Select(path => (Path: path, Text: File.ReadAllText(path))).ToArray();
            int replacements = 0;
            var changedSources = sources.Select(source =>
            {
                SyntaxNode syntax = CSharpSyntaxTree.ParseText(source.Text).GetRoot();
                LiteralExpressionSyntax[] constants = syntax.DescendantNodes().OfType<LiteralExpressionSyntax>()
                    .Where(literal => literal.IsKind(SyntaxKind.NumericLiteralExpression) && literal.Token.ValueText == "7"
                        && literal.Ancestors().OfType<InvocationExpressionSyntax>().Any(call =>
                            call.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "Calculate"))
                    .ToArray();
                replacements += constants.Length;
                return (source.Path, Text: syntax.ReplaceNodes(constants, (original, _) =>
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(8)).WithTriviaFrom(original)).ToFullString());
            }).ToArray();
            Assert.True(replacements > 0, "The fixture must alter at least one emitted default forwarding constant.");
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
                changedSources, configuration.library.nativeName));
            string concrete = sourceRuntime ? "DefaultApiContext" : "DefaultApi";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string initializeShared = mode == BindingImportMode.FunctionTable ? "DefaultApi.Load(binary);" : "DefaultApi.Create();";
            string shared = sourceRuntime ? initializeShared + "\nif (DefaultApi.Calculate(32) != 42 || DefaultApi.Plus(32) != 42 || DefaultApi.Numbers.Sum(32) != 42) return 3;\nDefaultApi.Dispose();" : "";
            string consumer = $$"""
                using System;
                using Generated.Defaults;
                using ApiContext = Generated.Defaults.{{concrete}};
                public static class DefaultsProbe {
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        if (api.Calculate(32, 7, "ABC") != 42 || api.Calculate(32, 7) != 42 || api.Calculate(32, "ABC") != 42 || api.Calculate(32) != 42) return 1;
                        if (api.Plus(32) != 42 || api.Numbers.Sum(32) != 42 || api.Numbers.Sum(32, "ABC") != 42) return 2;
                        if (!api.Identity().Equals(ulong.MaxValue) || !api.IdentityI().Equals(int.MaxValue)) return 4;
                        {{shared}}
                        api.Dispose();
                        try { _ = api.Calculate(32); return 5; } catch (ObjectDisposedException) { }
                        return 42;
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary,
                configuration.library.nativeName, consumer, "DefaultsProbe", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class NoWrites : IArtifactSink
    {
        public string WriteText(string relativePath, string content) => throw new InvalidOperationException("Planning cannot emit files.");
    }
    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
