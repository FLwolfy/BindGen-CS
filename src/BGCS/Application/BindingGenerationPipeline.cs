namespace BGCS.Application;

using BGCS.Analysis;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Logging;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Metadata;
using BGCS.Emission;
using BGCS.Intermediate;
using BGCS.Output;
using BGCS.PreProcessSteps;

/// <summary>
/// Coordinates declaration graph construction and shared IR analysis for generation frontends.
/// </summary>
public sealed class BindingGenerationPipeline
{
    private readonly CsCodeGeneratorConfig config;

    public BindingGenerationPipeline(CsCodeGeneratorConfig config)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
    }

    internal bool Generate(CsCodeGenerator generator, CppCompilation compilation, List<string> headerFiles,
        string outputPath, List<string>? allowedHeaders)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(compilation);
        ConfigValidator.Validate(config);
        if (generator.CLIOptions?.OutputDirectory != null)
            outputPath = Path.Combine(generator.CLIOptions.OutputDirectory, outputPath);
        generator.ReportCompilationDiagnostics(compilation);
        if (compilation.HasErrors)
        {
            generator.SetLastResult(CreateResult(compilation, headerFiles, false, outputPath, generator.Messages));
            return false;
        }

        allowedHeaders ??= generator.ResolveAllowedHeaders(compilation, headerFiles);
        bool explicitEmptyOutputFilter = allowedHeaders.Count == 0;
        FileSet files = new(allowedHeaders.Select(PathHelper.GetPath));
        using GeneratedOutputTransaction transaction = new(outputPath);
        string stagingPath = transaction.StagingPath;

        generator.LogInfo("Configuring Pre-Processing Steps...");
        foreach (PreProcessStep step in generator.PreProcessSteps)
            step.Configure(config);
        generator.LogInfo("Running Pre-Processing Steps...");
        ParseResult result = new(compilation);
        config.TypeConverter.Initialize(result);
        foreach (PreProcessStep step in generator.PreProcessSteps)
            step.PreProcess(files, compilation, config, generator.PipelineMetadata, result);

        generator.InvokePrePatch(result, headerFiles);
        BindingModule safetyModule = Analyze(compilation, allowedHeaders);
        if (safetyModule.StructuredDiagnostics.Any(diagnostic => diagnostic.Severity == BindingDiagnosticSeverity.Error))
        {
            generator.SetLastResult(new(safetyModule, false, [],
            [
                .. generator.Messages.Select(diagnostic => new BindingDiagnostic(
                    (BindingDiagnosticSeverity)(int)diagnostic.Severtiy, diagnostic.Message)),
                .. safetyModule.StructuredDiagnostics
            ]));
            return false;
        }
        if (!explicitEmptyOutputFilter)
        {
            CSharpEmitter emitter = new();
            IReadOnlyList<BindingDiagnostic> emissionDiagnostics = emitter.Validate(safetyModule);
            if (emissionDiagnostics.Count > 0)
            {
                foreach (BindingDiagnostic diagnostic in emissionDiagnostics)
                    safetyModule.StructuredDiagnostics.Add(diagnostic);
                generator.SetLastResult(new(safetyModule, false, [],
                [
                    .. generator.Messages.Select(diagnostic => new BindingDiagnostic(
                        (BindingDiagnosticSeverity)(int)diagnostic.Severtiy, diagnostic.Message)),
                    .. safetyModule.StructuredDiagnostics
                ]));
                return false;
            }
            string irFileName = config.MergeGeneratedFilesToSingleFile
                ? ".bgcs-ir.cs"
                : SingleFileOutputNameResolver.Resolve(config);
            emitter.Emit(safetyModule,
                new(stagingPath, config.MergeGeneratedFilesToSingleFile, irFileName,
                    ResolveRuntimeNamespace(), config.OneFilePerType));
        }
        foreach (var pluginEmitter in config.Plugins.GetServices<IBindingEmitter>())
            pluginEmitter.Service.Emit(safetyModule, new(stagingPath, config.MergeGeneratedFilesToSingleFile,
                config.SingleFileOutputName, ResolveRuntimeNamespace()));

        generator.LogInfo("Applying Post-Patches...");
        generator.PatchEngine.ApplyPostPatches(generator.PipelineMetadata, stagingPath,
            Directory.GetFiles(stagingPath, "*.*", SearchOption.AllDirectories).ToList());
        generator.RewriteGeneratedRuntimeUsings(stagingPath);
        CsCodeGenerator.RemoveEmptyGeneratedTypes(stagingPath);
        CsCodeGenerator.RemoveEmptyGeneratedDirectories(stagingPath);
        if (config.MergeGeneratedFilesToSingleFile)
            generator.ComposeSingleFile(stagingPath);
        if (config.GenerateRuntimeSource)
            generator.EmitStandaloneRuntime(stagingPath);

        transaction.Commit();
        string[] outputFiles = Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        generator.SetLastResult(new(safetyModule, true, outputFiles,
        [
            .. generator.Messages.Select(diagnostic => new BindingDiagnostic(
                (BindingDiagnosticSeverity)(int)diagnostic.Severtiy, diagnostic.Message)),
            .. safetyModule.StructuredDiagnostics
        ]));
        return true;
    }

    public BindingModule Analyze(CppCompilation compilation, IEnumerable<string> allowedHeaders)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(allowedHeaders);
        FileSet files = new(allowedHeaders.Select(PathHelper.GetPath));
        config.TypeConverter.Initialize(new ParseResult(compilation));
        DeclarationGraph graph = DeclarationGraph.Create(compilation, declaration => IsAllowed(declaration, files));
        IEnumerable<CppMacro> macros = compilation.Macros.Where(macro =>
            !string.IsNullOrWhiteSpace(macro.SourceFile) && files.Contains(macro.SourceFile));
        return new BindingModuleAnalyzer(config).Analyze(graph, macros);
    }

    public BindingGenerationResult CreateResult(CppCompilation compilation, IEnumerable<string> allowedHeaders,
        bool success, string outputPath, IReadOnlyList<LogMessage> diagnostics)
    {
        BindingModule? module = success ? Analyze(compilation, allowedHeaders) : null;
        IReadOnlyList<string> outputFiles = success && Directory.Exists(outputPath)
            ? Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        BindingDiagnostic[] resultDiagnostics =
        [
            .. diagnostics.Select(diagnostic => new BindingDiagnostic(
                (BindingDiagnosticSeverity)(int)diagnostic.Severtiy, diagnostic.Message)),
            .. (module?.StructuredDiagnostics ?? [])
        ];
        return new(module, success, outputFiles, resultDiagnostics);
    }

    private static bool IsAllowed(ICppDeclaration declaration, FileSet files)
    {
        return declaration is CppElement element && files.Contains(element.SourceFile);
    }

    private string ResolveRuntimeNamespace() => string.IsNullOrWhiteSpace(config.RuntimeNamespace)
        ? "BGCS.Runtime"
        : config.RuntimeNamespace;
}
