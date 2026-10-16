using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Targets;
using Xunit;

namespace BGCS.Tests;

internal static class NativeFactDriftAssertions
{
    internal static async Task<NativeModule> ObserveAsync(BindingHost host, BindingConfiguration configuration, string root,
        NativeTargetDescriptor target, ImmutableArray<string> arguments)
    {
        AnalysisResult observed = await host.AnalyzeAsync(new(configuration.source.headers.Select(path => Path.GetFullPath(path, root)).ToImmutableArray(),
            configuration.source.language, root)
        {
            targetId = target.targetId.value,
            triple = target.triple,
            toolchain = target.toolchain,
            languageStandard = configuration.source.languageStandard ?? (configuration.source.language == SourceLanguage.C ? "c17" : "c++23"),
            includeDirectories = configuration.source.includeDirectories,
            systemIncludeDirectories = configuration.source.systemIncludeDirectories,
            defines = configuration.source.defines,
            arguments = arguments,
            preamble = configuration.source.preamble,
            postamble = configuration.source.postamble,
            parseMacros = false
        });
        Assert.True(observed.success, observed.GetDiagnosticSummary());
        return Assert.IsType<NativeModule>(observed.module);
    }

    internal static NativeType Canonical(NativeModule module, BGCS.Contracts.Common.StableId identity)
    {
        NativeType type = module.GetType(identity);
        while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = module.GetType(type.elementType!.Value);
        return type;
    }

    internal static NativeType Return(NativeModule module)
        => Canonical(module, Assert.Single(module.declarations, declaration => declaration.kind == "Function" && declaration.name == "twice").returnType!.Value);

    internal static void Reject(BuildResult result, string cause)
    {
        Assert.False(result.success);
        Assert.False(result.published);
        string expected = "Native effective source facts differ from the generated contract: " + cause + ".";
        Assert.True(result.diagnostics.Where(diagnostic => diagnostic.isFailure).Select(diagnostic => diagnostic.message).SequenceEqual([expected]), result.GetDiagnosticSummary());
    }
}
