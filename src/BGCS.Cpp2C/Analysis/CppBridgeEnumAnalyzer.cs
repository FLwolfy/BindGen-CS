using System.Collections.Generic;
using System.Linq;
using BGCS.Core.IO;
using BGCS.Core.Writing;
using BGCS.Cpp2C.Configuration;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Declarations;
using BGCS.Intermediate.Bridges;

namespace BGCS.Cpp2C.Analysis;

/// <summary>
/// Lowers scoped and nested C++ enums into frozen C ABI declarations.
/// </summary>
internal sealed class CppBridgeEnumAnalyzer
{
    private readonly Cpp2CGeneratorConfig m_config;
    internal CppBridgeEnumAnalyzer(Cpp2CGeneratorConfig config)
    {
        this.m_config = config;
    }

    internal CppBridgeArtifact Analyze(
        ParseResult result,
        FileSet files
    ) {
        using CppBridgeOperationBuilder writer = new();
        WriteEnums(writer, result.compilation.enums.Where(value => files.Contains(value.sourceFile)));
        List<CppClass> classes = [.. result.compilation.classes];
        foreach (var ns in result.compilation.EnumerateNamespaces())
        {
            var name = ns.GetFullNamespace("::");
            writer.WriteLine($"// begin namespace {name}");
            WriteEnums(writer, ns.enums.Where(value => files.Contains(value.sourceFile)));
            classes.AddRange(ns.classes);
            writer.WriteLine($"// end namespace {name}");
        }

        foreach (CppClass cppClass in CppBridgeDeclarationPolicy.EnumerateAccessibleClasses(classes))
            WriteEnums(writer, cppClass.enums.Where(value => files.Contains(value.sourceFile)));
        return writer.Freeze("include/enums.h");
    }

    private void WriteEnums(
        ICodeWriter writer,
        IEnumerable<CppEnum> enums
    ) {
        foreach (var enumClass in enums.Where(CppBridgeDeclarationPolicy.IsAccessible))
        {
            WriteEnum(writer, enumClass);
        }
    }

    private void WriteEnum(
        ICodeWriter writer,
        CppEnum enumClass
    ) {
        Dictionary<string, string> map = [];
        string enumName = this.m_config.GetCTypeName(enumClass);
        foreach (var item in enumClass.items)
        {
            map[item.name] = $"{enumName}_{item.name}";
        }

        writer.BeginBlock("typedef enum");
        foreach (var item in enumClass.items)
        {
            WriteEnumItem(writer, map[item.name], item);
        }

        writer.EndBlock($"}} {enumName};");
    }

    private void WriteEnumItem(
        ICodeWriter writer,
        string enumName,
        CppEnumItem item
    ) {
        writer.WriteLine($"{enumName} = {item.value},");
    }
}
