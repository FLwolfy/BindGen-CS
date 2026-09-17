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
        WriteEnums(writer, result.Compilation.Enums);

        List<CppClass> classes = [.. result.Compilation.Classes];
        foreach (var ns in result.Compilation.EnumerateNamespaces())
        {
            var name = ns.GetFullNamespace("::");
            writer.WriteLine($"// begin namespace {name}");
            WriteEnums(writer, ns.Enums);
            classes.AddRange(ns.Classes);
            writer.WriteLine($"// end namespace {name}");
        }
        foreach (CppClass cppClass in EnumerateClasses(classes))
            WriteEnums(writer, cppClass.Enums);
    }

    private static IEnumerable<CppClass> EnumerateClasses(IEnumerable<CppClass> classes)
    {
        foreach (CppClass cppClass in classes)
        {
            yield return cppClass;
            foreach (CppClass nested in EnumerateClasses(cppClass.Classes))
                yield return nested;
        }
    }

    private void WriteEnums(CodeWriter writer, IEnumerable<CppEnum> enums)
    {
        foreach (var enumClass in enums)
        {
            WriteEnum(writer, enumClass);
        }
    }

    private void WriteEnum(ICodeWriter writer, CppEnum enumClass)
    {
        Dictionary<string, string> map = [];
        string enumName = config.GetCTypeName(enumClass);
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
