using BGCS.Core.IO;
using BGCS.Cpp2C.Analysis;
using BGCS.Cpp2C.Configuration;
using BGCS.Cpp2C.Emission;
using BGCS.Intermediate.Bridges;
using BGCS.Intermediate.Emission;

namespace BGCS.Cpp2C.Application;

/// <summary>
/// Owns the single transition from AST analysis to frozen bridge IR and source emission.
/// </summary>
internal sealed class CppBridgeGenerationPipeline
{
    private readonly Cpp2CGeneratorConfig m_config;
    private readonly ICppBridgeEmitter m_emitter;
    internal CppBridgeGenerationPipeline(
        Cpp2CGeneratorConfig config,
        ICppBridgeEmitter? emitter = null
    ) {
        m_config = config;
        m_emitter = emitter ?? new CBridgeEmitter();
    }

    internal CppBridgeModule Generate(
        FileSet files,
        ParseResult result,
        string stagingDirectory
    ) {
        m_config.lowerings.BeginGeneration();
        m_config.ClearAnalysisReferences();
        CppBridgeModule module;
        try
        {
            module = new CppBridgeModuleAnalyzer(m_config).Analyze(files, result);
        }
        finally
        {
            m_config.ClearAnalysisReferences();
        }
        EmissionContext context = new(stagingDirectory, false, string.Empty);
        m_emitter.Emit(module, context);
        foreach (var registration in m_config.plugins.GetServices<ICppBridgeEmitter>())
            registration.service.Emit(module, context);
        return module;
    }
}
