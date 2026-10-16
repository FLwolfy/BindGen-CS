using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

namespace BGCS.Tool.Validation;

internal static class ApiSnapshotValidator
{
    internal static int Run(
        string[] args,
        TextWriter output,
        TextWriter error
    ) {
        if (args.Length != 2)
        {
            error.WriteLine("Usage: bindgen-cs validate api-snapshot <assembly> <output>");
            return 2;
        }

        string assemblyPath = Path.GetFullPath(args[0]);
        string outputPath = Path.GetFullPath(args[1]);
        using SnapshotLoadContext context = new(assemblyPath);
        Assembly assembly = context.LoadFromAssemblyPath(assemblyPath);
        using FileStream input = File.OpenRead(assemblyPath);
        using PEReader pe = new(input);
        ApiSnapshotSignatureProvider signatures = new(pe.GetMetadataReader());
        List<string> lines = [];
        foreach (Type type in assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            string kind = type.IsEnum ? "enum" : type.IsValueType ? "struct" : type.IsInterface ? "interface" : "class";
            lines.Add($"{kind} {type.FullName}" + ApiSnapshotSurfaceFacts.Type(type, FormatType));
            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (FieldInfo field in type.GetFields(flags).OrderBy(field => field.Name, StringComparer.Ordinal))
                lines.Add($"  field {(HasFunctionPointer(field.FieldType) ? signatures.Field(field.MetadataToken) : FormatType(field.FieldType))} {field.Name}" + ApiSnapshotSurfaceFacts.Field(field));
            foreach (ConstructorInfo constructor in type.GetConstructors(flags).OrderBy(constructor => constructor.ToString(), StringComparer.Ordinal))
            {
                string[]? exact = constructor.GetParameters().Any(parameter => HasFunctionPointer(parameter.ParameterType))
                    ? signatures.Method(constructor.MetadataToken).ParameterTypes.Select(parameter => parameter.text).ToArray() : null;
                lines.Add($"  ctor({FormatParameters(constructor.GetParameters(), exact)})");
            }
            foreach (PropertyInfo property in type.GetProperties(flags).OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                List<string> accessors = [];
                if (property.GetMethod?.IsPublic == true)
                    accessors.Add("get");
                if (property.SetMethod?.IsPublic == true)
                    accessors.Add(property.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit") ? "init" : "set");
                if (accessors.Count > 0)
                {
                    ParameterInfo[] indices = property.GetIndexParameters();
                    string[]? exact = indices.Any(parameter => HasFunctionPointer(parameter.ParameterType))
                        ? signatures.Method((property.GetMethod ?? property.SetMethod!).MetadataToken).ParameterTypes.Take(indices.Length).Select(parameter => parameter.text).ToArray() : null;
                    string indexer = indices.Length == 0 ? string.Empty : "[" + FormatParameters(indices, exact) + "]";
                    lines.Add($"  property {(HasFunctionPointer(property.PropertyType) ? signatures.Property(property.MetadataToken) : FormatType(property.PropertyType))} {property.Name}{indexer} {{ {string.Join("; ", accessors)} }}" + ApiSnapshotSurfaceFacts.Method(property.GetMethod ?? property.SetMethod!, FormatType));
                }
            }

            foreach (EventInfo eventInfo in type.GetEvents(flags).OrderBy(eventInfo => eventInfo.Name, StringComparer.Ordinal))
                lines.Add($"  event {FormatType(eventInfo.EventHandlerType!)} {eventInfo.Name}" + ApiSnapshotSurfaceFacts.Method(eventInfo.AddMethod ?? eventInfo.RemoveMethod!, FormatType));
            foreach (MethodInfo method in type.GetMethods(flags).Where(method => !method.IsSpecialName || method.Name.StartsWith("op_", StringComparison.Ordinal)).OrderBy(method => method.ToString(), StringComparer.Ordinal))
            {
                bool exact = HasFunctionPointer(method.ReturnType) || method.GetParameters().Any(parameter => HasFunctionPointer(parameter.ParameterType));
                MethodSignature<ApiSnapshotSignatureProvider.SignatureType>? signature = exact ? signatures.Method(method.MetadataToken) : null;
                lines.Add($"  method {signature?.ReturnType.text ?? FormatType(method.ReturnType)} {method.Name}({FormatParameters(method.GetParameters(), signature?.ParameterTypes.Select(parameter => parameter.text).ToArray())})" + ApiSnapshotSurfaceFacts.Method(method, FormatType));
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        return 0;
    }

    private static bool HasFunctionPointer(Type type) => type.IsFunctionPointer
        || type.HasElementType && HasFunctionPointer(type.GetElementType()!)
        || type.IsGenericType && type.GetGenericArguments().Any(HasFunctionPointer);
    private static string FormatParameters(
        IEnumerable<ParameterInfo> parameters,
        string[]? exact = null
    )
        => string.Join(", ", parameters.Select(parameter =>
        {
            Type type = parameter.ParameterType;
            string modifier = parameter.IsOut ? "out " : type.IsByRef ? parameter.IsIn ? "in " : "ref " : string.Empty;
            string name = exact is null ? FormatType(type.IsByRef ? type.GetElementType()! : type)
                : type.IsByRef ? exact[parameter.Position].TrimEnd('&') : exact[parameter.Position];
            return $"{modifier}{name} {parameter.Name}" + ApiSnapshotSurfaceFacts.Parameter(parameter);
        }));
    private static string FormatType(Type type)
    {
        if (type.IsFunctionPointer)
        {
            string convention = type.IsUnmanagedFunctionPointer ? " unmanaged" : " managed";
            Type[] conventions = type.GetFunctionPointerCallingConventions();
            if (conventions.Length > 0)
                convention += "[" + string.Join(",", conventions.Select(FormatType)) + "]";
            IEnumerable<Type> signature = type.GetFunctionPointerParameterTypes().Append(type.GetFunctionPointerReturnType());
            return "delegate*" + convention + "<" + string.Join(",", signature.Select(FormatType)) + ">";
        }
        if (type.IsByRef)
            return FormatType(type.GetElementType()!) + "&";
        if (type.IsPointer)
            return FormatType(type.GetElementType()!) + "*";
        if (type.IsArray)
            return FormatType(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (!type.IsGenericType)
            return type.FullName ?? type.Name;
        string name = type.GetGenericTypeDefinition().FullName!;
        name = Regex.Replace(name, @"`\d+", string.Empty);
        return name + "<" + string.Join(",", type.GetGenericArguments().Select(FormatType)) + ">";
    }
}
