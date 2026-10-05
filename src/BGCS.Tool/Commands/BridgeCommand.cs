using System;
using System.IO;
using System.Linq;
using BGCS.Configuration;
using BGCS.Core.IO;
using BGCS.Cpp2C.Configuration;
using BGCS.Cpp2C.Facade;
using BGCS.Cpp2C.Lowering;
using BGCS.Facade;
using BGCS.Intermediate.Bridges;
using BGCS.Tool.Output;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BGCS.Tool.Commands;

internal static class BridgeCommand
{
    internal static int Run(string[] args)
    {
        (string configPath, string? outputPath) = GenerationArguments.Parse(args);
        Cpp2CGeneratorConfig config = Cpp2CGeneratorConfig.Load(configPath);
        string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        string nativeOutput = Path.GetFullPath(outputPath ?? config.outputPath, baseDirectory);
        string managedOutput = Path.GetFullPath(config.cSharpOutputPath, baseDirectory);
        config.outputPath = nativeOutput;
        using OutputDirectorySetTransaction publication = new(config.generateCSharpBindings
            ? [nativeOutput, managedOutput] : [nativeOutput]);
        Cpp2CCodeGenerator generator = new(config);
        using var logging = GenerationDiagnosticWriter.Attach(generator, Console.Out);
        generator.GenerateConfigured(publication.GetStagingPath(nativeOutput));
        if (generator.lastResult?.success != true)
        {
            GenerationDiagnosticWriter.WriteFailure(generator.lastResult, Console.Error);
            return 1;
        }

        if (config.generateCSharpBindings)
        {
            string bridgeHeader = generator.lastResult.outputFiles.Single(path => string.Equals(Path.GetFileName(path), "Classes.h", StringComparison.OrdinalIgnoreCase));
            string bridgeInclude = Path.GetDirectoryName(bridgeHeader)!;
            CsCodeGeneratorConfig csharpConfig = new()
            {
                @namespace = config.cSharpNamespace,
                apiName = config.cSharpApiName,
                libName = config.nativeLibraryName,
                strictSafetySeverity = config.cSharpStrictSafetySeverity,
                targetId = config.targetId,
                targetResolver = config.targetResolver,
                targetTriple = config.targetTriple,
                targetSysRoot = config.targetSysRoot,
                compilerPath = config.compilerPath,
                parserKind = BGCS.CppAst.Parsing.CppParserKind.C,
                autoSquashTypedef = false,
                parseMacros = false,
                parseComments = false,
                parseSystemIncludes = false,
                delegatesAsVoidPointer = true,
                includeTransitivelyReferencedHeaders = true,
                importType = ImportType.DllImport,
                generateExtensions = false,
                oneFilePerType = false,
                mergeGeneratedFilesToSingleFile = true,
                outputPath = managedOutput
            };
            csharpConfig.includeFolders.Add(bridgeInclude);
            new global::BGCS.Configuration.ConfigComposer().Compose(ref csharpConfig, baseDirectory);
            BGCS.Configuration.ConfigValidator.Validate(csharpConfig);
            CsCodeGenerator csharpGenerator = new(csharpConfig);
            using var csharpLogging = GenerationDiagnosticWriter.Attach(csharpGenerator, Console.Out);
            string managedCandidate = publication.GetStagingPath(managedOutput);
            if (!csharpGenerator.Generate(bridgeHeader, managedCandidate))
            {
                GenerationDiagnosticWriter.WriteFailure(csharpGenerator.lastResult, Console.Error);
                return 1;
            }

            CppManagedArtifactPlan managedPlan = generator.lastResult.module!.managedArtifacts
                ?? throw new InvalidOperationException("Bridge analysis did not produce the requested managed artifact plan.");
            CppExtensionArtifactEmitter.EmitManaged(managedPlan, csharpGenerator.lastResult!.module!, new(managedCandidate, false, string.Empty));
            ValidateManagedSources(managedCandidate);
        }
        publication.Commit();
        Console.WriteLine($"Generated C++ bridge with {generator.lastResult.module!.types.Count} types and {generator.lastResult.module.functions.Count} functions in {nativeOutput}.");
        if (config.generateCSharpBindings)
            Console.WriteLine($"Generated C# bridge bindings in {managedOutput}.");
        return 0;
    }

    private static void ValidateManagedSources(string outputPath)
    {
        foreach (string path in Directory.EnumerateFiles(outputPath, "*.cs", SearchOption.AllDirectories))
        {
            Diagnostic[] errors = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
                throw new InvalidOperationException($"Managed bridge output is syntactically invalid: {string.Join(Environment.NewLine, errors.Select(error => error.ToString()))}");
        }
    }
}
