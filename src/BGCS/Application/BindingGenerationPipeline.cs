using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Analysis.PreProcessing;
using BGCS.Facade;
using BGCS.Intermediate.Emission;

namespace BGCS.Application;

using BGCS.Analysis;
using BGCS.Configuration;
using BGCS.Core.IO;
using BGCS.Core.Logging;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Metadata;
using BGCS.Emission;
using BGCS.Intermediate;
using BGCS.Output;

/// <summary>
/// Coordinates declaration graph construction and shared IR analysis for generation frontends.
/// </summary>
internal sealed class BindingGenerationPipeline
{
    private readonly CsCodeGeneratorConfig m_config;
    internal BindingGenerationPipeline(CsCodeGeneratorConfig config)
    {
        m_config = config ?? throw new ArgumentNullException(nameof(config));
    }

    internal bool Generate(
        CsCodeGenerator generator,
        CppCompilation compilation,
        List<string> headerFiles,
        string outputPath,
        List<string>? allowedHeaders
    ) {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(compilation);
        ConfigValidator.Validate(m_config);
        generator.ReportCompilationDiagnostics(compilation);
        if (compilation.hasErrors)
        {
            generator.SetLastResult(CreateResult(compilation, headerFiles, false, outputPath, generator.messages));
            return false;
        }

        allowedHeaders ??= generator.ResolveAllowedHeaders(compilation, headerFiles);
        bool explicitEmptyOutputFilter = allowedHeaders.Count == 0;
        FileSet files = new(allowedHeaders);
        using OutputDirectoryTransaction transaction = new(outputPath);
        string stagingPath = transaction.stagingPath;
        ParseResult result = PrepareModel(generator, compilation, files);
        BindingModule safetyModule = Analyze(result, allowedHeaders);
        if (safetyModule.structuredDiagnostics.Any(diagnostic => diagnostic.severity == BindingDiagnosticSeverity.Error))
        {
            generator.SetLastResult(new(safetyModule, false, [], [.. generator.messages.Select(diagnostic => new BindingDiagnostic((BindingDiagnosticSeverity)(int)diagnostic.severity, diagnostic.message)), .. safetyModule.structuredDiagnostics]));
            return false;
        }

        if (!explicitEmptyOutputFilter)
        {
            CSharpEmitter emitter = new();
            IReadOnlyList<BindingDiagnostic> emissionDiagnostics = emitter.Validate(safetyModule);
            if (emissionDiagnostics.Count > 0)
            {
                generator.SetLastResult(new(safetyModule, false, [], [.. generator.messages.Select(diagnostic => new BindingDiagnostic((BindingDiagnosticSeverity)(int)diagnostic.severity, diagnostic.message)), .. safetyModule.structuredDiagnostics, .. emissionDiagnostics]));
                return false;
            }

            string irFileName = m_config.mergeGeneratedFilesToSingleFile ? ".bgcs-ir.cs" : SingleFileOutputNameResolver.Resolve(m_config);
            emitter.Emit(safetyModule, new(stagingPath, m_config.mergeGeneratedFilesToSingleFile, irFileName, ResolveRuntimeNamespace(), m_config.oneFilePerType));
        }

        foreach (var pluginEmitter in m_config.plugins.GetServices<IBindingEmitter>())
            pluginEmitter.service.Emit(safetyModule, new(stagingPath, m_config.mergeGeneratedFilesToSingleFile, m_config.singleFileOutputName, ResolveRuntimeNamespace()));
        generator.LogInfo("Applying Post-Patches...");
        generator.patchEngine.ApplyPostPatches(generator.pipelineMetadata, stagingPath, Directory.GetFiles(stagingPath, "*.*", SearchOption.AllDirectories).ToList());
        generator.RewriteGeneratedRuntimeUsings(stagingPath);
        CsCodeGenerator.RemoveEmptyGeneratedTypes(stagingPath);
        CsCodeGenerator.RemoveEmptyGeneratedDirectories(stagingPath);
        if (m_config.mergeGeneratedFilesToSingleFile)
            generator.ComposeSingleFile(stagingPath);
        if (m_config.generateRuntimeSource)
            generator.EmitStandaloneRuntime(stagingPath);
        transaction.Commit();
        string[] outputFiles = Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        generator.SetLastResult(new(safetyModule, true, outputFiles, [.. generator.messages.Select(diagnostic => new BindingDiagnostic((BindingDiagnosticSeverity)(int)diagnostic.severity, diagnostic.message)), .. safetyModule.structuredDiagnostics]));
        return true;
    }

    internal BindingModule Analyze(
        ParseResult result,
        IEnumerable<string> allowedHeaders
    ) {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(allowedHeaders);
        CppCompilation compilation = result.compilation;
        FileSet files = new(allowedHeaders);
        m_config.typeConverter.Initialize(result);
        DeclarationGraph graph = DeclarationGraph.Create(compilation, declaration => IsAllowed(declaration, files));
        IEnumerable<CppMacro> macros = compilation.macros.Where(macro => !string.IsNullOrWhiteSpace(macro.sourceFile) && files.Contains(macro.sourceFile));
        return new BindingModuleAnalyzer(m_config).Analyze(graph, macros, result.functionAliases.Values.SelectMany(aliases => aliases));
    }

    internal ParseResult PrepareModel(
        CsCodeGenerator generator,
        CppCompilation compilation,
        FileSet files
    ) {
        generator.LogInfo("Running Pre-Processing Steps...");
        ParseResult result = new(compilation);
        m_config.typeConverter.Initialize(result);
        foreach (PreProcessStep step in generator.preProcessSteps)
            step.PreProcess(files, compilation, m_config, generator.pipelineMetadata, result);
        generator.InvokePrePatch(result);
        return result;
    }

    internal BindingGenerationResult<BindingModule> CreateResult(
        CppCompilation compilation,
        IEnumerable<string> allowedHeaders,
        bool success,
        string outputPath,
        IReadOnlyList<LogMessage> diagnostics
    ) {
        BindingModule? module = success ? Analyze(new ParseResult(compilation), allowedHeaders) : null;
        IReadOnlyList<string> outputFiles = success && Directory.Exists(outputPath) ? Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray() : [];
        BindingDiagnostic[] resultDiagnostics = [.. diagnostics.Select(diagnostic => new BindingDiagnostic((BindingDiagnosticSeverity)(int)diagnostic.severity, diagnostic.message)), .. (module?.structuredDiagnostics ?? [])];
        return new(module, success, outputFiles, resultDiagnostics);
    }

    private static bool IsAllowed(
        ICppDeclaration declaration,
        FileSet files
    ) {
        return declaration is CppElement element && files.Contains(element.sourceFile);
    }

    private string ResolveRuntimeNamespace() => string.IsNullOrWhiteSpace(m_config.runtimeNamespace) ? "BGCS.Runtime" : m_config.runtimeNamespace;
}
