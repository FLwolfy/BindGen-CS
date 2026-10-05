using System.Collections.Generic;
using System.Linq;
using BGCS.Core.IO;
using BGCS.Cpp2C.Configuration;
using BGCS.Cpp2C.Lowering;
using BGCS.Intermediate;
using BGCS.Intermediate.Bridges;

namespace BGCS.Cpp2C.Analysis;

/// <summary>
/// Completes AST-dependent ABI analysis and lowering before any native source is emitted.
/// </summary>
internal sealed class CppBridgeModuleAnalyzer
{
    private readonly Cpp2CGeneratorConfig m_config;
    internal CppBridgeModuleAnalyzer(Cpp2CGeneratorConfig config) => m_config = config;
    internal CppBridgeModule Analyze(
        FileSet files,
        ParseResult result
    ) {
        List<CppBridgeArtifact> artifacts = [new CppBridgeEnumAnalyzer(m_config).Analyze(result, files)];
        artifacts.AddRange(new CppBridgeClassAnalyzer(m_config).Analyze(files, result));
        IReadOnlyList<CppBridgeArtifact> extensions = CppExtensionArtifactEmitter.CollectNative(m_config);
        artifacts.AddRange(extensions);
        CppBridgeArtifact[] exposed = extensions.Where(static artifact => artifact.exposeToBindings).OrderBy(static artifact => artifact.relativePath, System.StringComparer.Ordinal).ToArray();
        if (exposed.Length > 0)
        {
            int umbrellaIndex = artifacts.FindIndex(static artifact => artifact.relativePath == "include/Classes.h");
            CppBridgeArtifact umbrella = artifacts[umbrellaIndex];
            artifacts[umbrellaIndex] = new(umbrella.relativePath, umbrella.operations.Concat(exposed.Select(static artifact => new CppBridgeOperation(CppBridgeOperationKind.Line, $"#include \"{artifact.relativePath["include/".Length..]}\""))));
        }

        BindingModule abi = new CppBridgeAbiAnalyzer(m_config).Analyze(result.compilation, files);
        return new(m_config.resolvedTarget.targetId.value, abi.types.Select(static type => new CppBridgeType(type.nativeName, type.managedName, type.kind, type.size, type.alignment)), abi.functions.Select(static function => new CppBridgeFunction(function.nativeName, function.managedName, function.kind, function.returnType, function.returnMarshalling, function.parameters)), artifacts, abi.structuredDiagnostics)
        {
            managedArtifacts = m_config.generateCSharpBindings ? CppExtensionArtifactEmitter.CollectManaged(m_config) : null
        };
    }
}
