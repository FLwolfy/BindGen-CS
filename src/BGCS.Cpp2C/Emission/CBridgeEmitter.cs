namespace BGCS.Cpp2C.Emission;

using System.Text;
using BGCS.Core;
using BGCS.Cpp2C.Metadata;
using BGCS.Cpp2C.Lowering;
using BGCS.Intermediate;
using SharedEmissionContext = BGCS.Emission.EmissionContext;
using SharedEmitter = BGCS.Emission.IBindingEmitter;

/// <summary>
/// Emits C ABI declarations from shared binding IR. The AST bridge pipeline uses <see cref="EmitAst"/>
/// only for C++ constructs that are not yet representable in the shared C-facing IR.
/// </summary>
public sealed class CBridgeEmitter : SharedEmitter
{
    /// <inheritdoc />
    public string Name => "C++ to C bridge emitter";

    /// <inheritdoc />
    public IReadOnlyList<string> Emit(BindingModule module, SharedEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(context);
        Directory.CreateDirectory(context.OutputPath);
        string outputFile = Path.Combine(context.OutputPath, context.SingleFileName);
        StringBuilder writer = new();
        writer.AppendLine("#pragma once");
        writer.AppendLine("#include <stdint.h>");
        writer.AppendLine("#ifdef __cplusplus");
        writer.AppendLine("extern \"C\" {");
        writer.AppendLine("#endif");
        HashSet<string> declaredTypes = new(StringComparer.Ordinal);
        foreach (BindingType type in module.Types.Where(type => type.Kind == BindingTypeKind.OpaqueHandle))
        {
            if (declaredTypes.Add(type.ManagedName))
                writer.Append("typedef struct ").Append(type.ManagedName).Append(' ').Append(type.ManagedName).AppendLine(";");
        }
        foreach (BindingFunction function in module.Functions)
        {
            writer.Append(function.ReturnType.ManagedName).Append(' ').Append(function.ManagedName).Append('(');
            List<string> parameters = [];
            if (function.Kind == BindingFunctionKind.Instance && function.DeclaringType != null)
            {
                BindingType? declaringType = module.Types.FirstOrDefault(type => type.NativeName == function.DeclaringType);
                if (declaringType != null)
                    parameters.Add(declaringType.ManagedName + "* self");
            }
            parameters.AddRange(function.Parameters.Select(parameter => parameter.Type.ManagedName + " " + parameter.ManagedName));
            writer.Append(parameters.Count == 0 ? "void" : string.Join(", ", parameters));
            writer.AppendLine(");");
        }
        writer.AppendLine("#ifdef __cplusplus");
        writer.AppendLine("}");
        writer.AppendLine("#endif");
        File.WriteAllText(outputFile, writer.ToString());
        return [outputFile];
    }

    internal void EmitAst(Cpp2CCodeGenerator generator, IReadOnlyList<GenerationStep> steps, FileSet files,
        ParseResult result, string outputPath, Cpp2CGeneratorConfig config, Cpp2CGeneratorMetadata metadata)
    {
        generator.LogInfo("Configuring Steps...");
        foreach (GenerationStep step in steps)
            step.Configure(config);
        foreach (GenerationStep step in steps.Where(step => step.Enabled))
        {
            generator.LogInfo($"Generating {step.Name}...");
            step.Generate(files, result, outputPath, config, metadata);
            step.CopyToMetadata(metadata);
        }
        CppExtensionArtifactEmitter.EmitNative(config, outputPath);
    }
}
