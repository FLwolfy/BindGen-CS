using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace BGCS.RepositoryTools.Validation;

/// <summary>Reads API-significant metadata without invoking user constructors, properties or field initializers.</summary>
internal static class ApiSnapshotSurfaceFacts
{
    internal static string Type(
        Type type,
        Func<Type, string> format
    ) {
        List<string> facts = [];
        if (type.IsAbstract) facts.Add("abstract");
        if (type.IsSealed) facts.Add("sealed");
        if (type.IsByRefLike) facts.Add("ref-like");
        if (type.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute")) facts.Add("readonly");
        if (type.IsEnum) facts.Add("underlying=" + format(Enum.GetUnderlyingType(type)));
        else if (type.BaseType is { } parent && parent != typeof(object) && parent != typeof(ValueType)) facts.Add("base=" + format(parent));
        Type[] interfaces = type.GetInterfaces();
        if (interfaces.Length != 0) facts.Add("interfaces=" + string.Join("|", interfaces.Select(format).Order(StringComparer.Ordinal)));
        if (type.IsGenericTypeDefinition) facts.AddRange(Constraints(type.GetGenericArguments(), format));
        return Suffix(facts);
    }

    internal static string Field(FieldInfo field)
    {
        List<string> facts = [];
        if (field.IsStatic) facts.Add("static");
        if (field.IsInitOnly) facts.Add("readonly");
        if (field.IsLiteral) facts.Add("const=" + Literal(field.GetRawConstantValue()));
        else if (field.CustomAttributes.SingleOrDefault(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.DecimalConstantAttribute") is { } constant)
        {
            IList<CustomAttributeTypedArgument> arguments = constant.ConstructorArguments;
            if (arguments.Count != 5) throw new InvalidOperationException("A decimal API constant has malformed metadata.");
            uint Bits(int index) => arguments[index].Value is int signed ? unchecked((uint)signed) : (uint)arguments[index].Value!;
            decimal value = new(unchecked((int)Bits(4)), unchecked((int)Bits(3)), unchecked((int)Bits(2)), (byte)arguments[1].Value! != 0, (byte)arguments[0].Value!);
            facts.Add("const=" + value.ToString(CultureInfo.InvariantCulture));
        }
        return Suffix(facts);
    }

    internal static string Method(
        MethodInfo method,
        Func<Type, string> format
    ) {
        List<string> facts = [];
        if (method.IsStatic) facts.Add("static");
        if (method.IsAbstract) facts.Add("abstract");
        if (method.IsVirtual) facts.Add("virtual");
        if (method.IsFinal) facts.Add("final");
        foreach (Type modifier in method.ReturnParameter.GetRequiredCustomModifiers()) facts.Add("return-modreq=" + format(modifier));
        foreach (Type modifier in method.ReturnParameter.GetOptionalCustomModifiers()) facts.Add("return-modopt=" + format(modifier));
        if (method.IsGenericMethodDefinition) facts.AddRange(Constraints(method.GetGenericArguments(), format));
        return Suffix(facts);
    }

    internal static string Parameter(ParameterInfo parameter)
    {
        List<string> facts = [];
        if (parameter.IsOptional) facts.Add("optional");
        if (parameter.HasDefaultValue) facts.Add("default=" + Literal(parameter.RawDefaultValue));
        foreach (Type modifier in parameter.GetRequiredCustomModifiers()) facts.Add("modreq=" + modifier.FullName);
        foreach (Type modifier in parameter.GetOptionalCustomModifiers()) facts.Add("modopt=" + modifier.FullName);
        if (parameter.CustomAttributes.Any(attribute => attribute.AttributeType.FullName is "System.ParamArrayAttribute" or "System.Runtime.CompilerServices.ParamCollectionAttribute")) facts.Add("params");
        return Suffix(facts);
    }

    private static IEnumerable<string> Constraints(
        IEnumerable<Type> parameters,
        Func<Type, string> format
    ) {
        foreach (Type parameter in parameters)
        {
            string attributes = parameter.GenericParameterAttributes.ToString();
            string[] bounds = parameter.GetGenericParameterConstraints().Select(format).Order(StringComparer.Ordinal).ToArray();
            if (parameter.GenericParameterAttributes != GenericParameterAttributes.None || bounds.Length != 0)
                yield return "generic " + parameter.Name + "=" + attributes + (bounds.Length == 0 ? string.Empty : ":" + string.Join("|", bounds));
            if (parameter.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsUnmanagedAttribute"))
                yield return "unmanaged " + parameter.Name;
        }
    }

    private static string Literal(object? value)
    {
        if (value is null) return "null";
        if (value is Missing) return "missing";
        if (value is DBNull) return "dbnull";
        if (value is string text) return JsonSerializer.Serialize(text);
        if (value is char character) return "U+" + ((int)character).ToString("X4", CultureInfo.InvariantCulture);
        if (value is bool boolean) return boolean ? "true" : "false";
        if (value is float single) return single.ToString("R", CultureInfo.InvariantCulture) + ":0x" + BitConverter.SingleToUInt32Bits(single).ToString("x8", CultureInfo.InvariantCulture);
        if (value is double number) return number.ToString("R", CultureInfo.InvariantCulture) + ":0x" + BitConverter.DoubleToUInt64Bits(number).ToString("x16", CultureInfo.InvariantCulture);
        if (value.GetType().IsEnum) return value.GetType().FullName + ":" + Convert.ToString(Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? throw new InvalidOperationException("An API default has no invariant representation.");
    }

    private static string Suffix(IEnumerable<string> facts)
    {
        string value = string.Join("; ", facts);
        return value.Length == 0 ? string.Empty : " [" + value + "]";
    }
}
