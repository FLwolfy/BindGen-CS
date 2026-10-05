using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        List<string> lines = [];
        foreach (Type type in assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            string kind = type.IsEnum ? "enum" : type.IsValueType ? "struct" : type.IsInterface ? "interface" : "class";
            lines.Add($"{kind} {type.FullName}");
            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (FieldInfo field in type.GetFields(flags).OrderBy(field => field.Name, StringComparer.Ordinal))
                lines.Add($"  field {FormatType(field.FieldType)} {field.Name}");
            foreach (ConstructorInfo constructor in type.GetConstructors(flags).OrderBy(constructor => constructor.ToString(), StringComparer.Ordinal))
                lines.Add($"  ctor({FormatParameters(constructor.GetParameters())})");
            foreach (PropertyInfo property in type.GetProperties(flags).OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                List<string> accessors = [];
                if (property.GetMethod?.IsPublic == true)
                    accessors.Add("get");
                if (property.SetMethod?.IsPublic == true)
                    accessors.Add("set");
                if (accessors.Count > 0)
                    lines.Add($"  property {FormatType(property.PropertyType)} {property.Name} {{ {string.Join("; ", accessors)} }}");
            }

            foreach (EventInfo eventInfo in type.GetEvents(flags).OrderBy(eventInfo => eventInfo.Name, StringComparer.Ordinal))
                lines.Add($"  event {FormatType(eventInfo.EventHandlerType!)} {eventInfo.Name}");
            foreach (MethodInfo method in type.GetMethods(flags).Where(method => !method.IsSpecialName).OrderBy(method => method.ToString(), StringComparer.Ordinal))
                lines.Add($"  method {FormatType(method.ReturnType)} {method.Name}({FormatParameters(method.GetParameters())})");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllLines(outputPath, lines, new UTF8Encoding(false));
        return 0;
    }

    private static string FormatParameters(IEnumerable<ParameterInfo> parameters)
        => string.Join(", ", parameters.Select(parameter =>
        {
            Type type = parameter.ParameterType;
            string modifier = parameter.IsOut ? "out " : type.IsByRef ? parameter.IsIn ? "in " : "ref " : string.Empty;
            return $"{modifier}{FormatType(type.IsByRef ? type.GetElementType()! : type)} {parameter.Name}";
        }));
    private static string FormatType(Type type)
    {
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
