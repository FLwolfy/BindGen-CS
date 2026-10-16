using System;
using System.Reflection;

namespace BGCS.CSharp.Tests;

internal static class FactoryInvocation
{
    internal static object? InvokeFactory(this MethodInfo method, object?[] arguments)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (arguments.Length > parameters.Length) throw new ArgumentException("Too many explicit factory arguments.", nameof(arguments));
        object?[] supplied = new object?[parameters.Length];
        Array.Copy(arguments, supplied, arguments.Length);
        for (int index = arguments.Length; index < parameters.Length; ++index)
        {
            if (!parameters[index].IsOptional) throw new ArgumentException("A required factory argument was omitted.", nameof(arguments));
            supplied[index] = Type.Missing;
        }
        return method.Invoke(null, supplied);
    }
}
