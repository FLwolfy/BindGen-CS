using BGCS.Core;
using BGCS.Cpp2C.Metadata;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;

namespace BGCS.Cpp2C.GenerationSteps;

/// <summary>
/// Defines the public class <c>EnumGenerationStep</c>.
/// </summary>
public class EnumGenerationStep : GenerationStep
{
    /// <summary>
    /// Initializes a new instance of <see cref="EnumGenerationStep"/>.
    /// </summary>
    public EnumGenerationStep(Cpp2CCodeGenerator generator, Cpp2CGeneratorConfig config) : base(generator, config)
    {
    }

    /// <summary>
    /// Gets <c>Name</c>.
    /// </summary>
    public override string Name { get; } = "Enums";

    /// <summary>
    /// Executes public operation <c>Configure</c>.
    /// </summary>
    public override void Configure(Cpp2CGeneratorConfig config)
    {
    }

    /// <summary>
    /// Executes public operation <c>CopyToMetadata</c>.
    /// </summary>
    public override void CopyToMetadata(Cpp2CGeneratorMetadata metadata)
    {
    }

    /// <summary>
    /// Executes public operation <c>CopyFromMetadata</c>.
    /// </summary>
    public override void CopyFromMetadata(Cpp2CGeneratorMetadata metadata)
    {
    }

    /// <summary>
    /// Executes public operation <c>Reset</c>.
    /// </summary>
    public override void Reset()
    {
    }

    /// <summary>
    /// Runs generation logic through <c>Generate</c>.
    /// </summary>
    public override void Generate(FileSet files, ParseResult result, string outputPath, Cpp2CGeneratorConfig config, Cpp2CGeneratorMetadata metadata)
    {
        var fileName = Path.Combine(outputPath, "include", "enums.h");
        using CodeWriter writer = new(fileName, "", null);
        WriteEnums(config.NamePrefix, writer, result.Compilation.Enums);

        foreach (var ns in result.Compilation.EnumerateNamespaces())
        {
            var name = ns.GetFullNamespace("::");
            writer.WriteLine($"// begin namespace {name}");
            WriteEnums(ns.GetFullNamespace("_"), writer, ns.Enums);
            writer.WriteLine($"// end namespace {name}");
        }
    }

    private void WriteEnums(string prefix, CodeWriter writer, IEnumerable<CppEnum> enums)
    {
        foreach (var enumClass in enums)
        {
            WriteEnum(prefix, writer, enumClass);
        }
    }

    private void WriteEnum(string prefix, ICodeWriter writer, CppEnum enumClass)
    {
        Dictionary<string, string> map = [];
        string enumName = $"{prefix}{enumClass.Name}";
        foreach (var item in enumClass.Items)
        {
            map[item.Name] = $"{enumName}_{item.Name}";
        }

        writer.BeginBlock("typedef enum");
        foreach (var item in enumClass.Items)
        {
            WriteEnumItem(writer, map[item.Name], item);
        }
        writer.EndBlock($"}} {enumName};");
    }

    private void WriteEnumItem(ICodeWriter writer, string enumName, CppEnumItem item)
    {
        writer.WriteLine($"{enumName} = {item.Value},");
    }
}
