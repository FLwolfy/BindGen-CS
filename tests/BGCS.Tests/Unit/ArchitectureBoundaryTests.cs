using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BGCS.Intermediate;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Tests;

public class ArchitectureBoundaryTests
{
    [Fact]
    public void IntermediatePublicSurface_ShouldNotDependOnOuterLayersOrInfrastructure()
    {
        string[] forbiddenPrefixes =
        [
            "BGCS.Facade",
            "BGCS.Application",
            "BGCS.Analysis",
            "BGCS.Emission",
            "BGCS.Configuration",
            "Microsoft.CodeAnalysis",
            "System.IO"
        ];
        Type[] intermediateTypes = typeof(BindingModule).Assembly.GetTypes()
            .Where(type => type.IsPublic && type.Namespace == "BGCS.Intermediate")
            .ToArray();

        foreach (Type type in intermediateTypes)
        {
            foreach (Type referencedType in GetPublicSignatureTypes(type))
            {
                string referencedNamespace = referencedType.Namespace ?? string.Empty;
                Assert.DoesNotContain(forbiddenPrefixes, prefix => referencedNamespace.StartsWith(prefix, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void IntermediateAssembly_ShouldHaveNoBgcsProjectDependencies()
    {
        AssemblyName[] references = typeof(BindingModule).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name?.StartsWith("BGCS", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(references, reference => reference.Name?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void RuntimeAssembly_ShouldNotReferenceGeneratorAssemblies()
    {
        AssemblyName[] references = typeof(Bool8).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name?.StartsWith("BGCS", StringComparison.Ordinal) == true);
    }

    private static IEnumerable<Type> GetPublicSignatureTypes(Type type)
    {
        foreach (ConstructorInfo constructor in type.GetConstructors())
            foreach (ParameterInfo parameter in constructor.GetParameters())
                foreach (Type referenced in Expand(parameter.ParameterType))
                    yield return referenced;
        foreach (PropertyInfo property in type.GetProperties())
            foreach (Type referenced in Expand(property.PropertyType))
                yield return referenced;
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            foreach (Type referenced in Expand(method.ReturnType))
                yield return referenced;
            foreach (ParameterInfo parameter in method.GetParameters())
                foreach (Type referenced in Expand(parameter.ParameterType))
                    yield return referenced;
        }
    }

    private static IEnumerable<Type> Expand(Type type)
    {
        if (type.IsArray)
        {
            foreach (Type nested in Expand(type.GetElementType()!))
                yield return nested;
            yield break;
        }
        if (type.IsGenericType)
        {
            yield return type.GetGenericTypeDefinition();
            foreach (Type argument in type.GetGenericArguments())
                foreach (Type nested in Expand(argument))
                    yield return nested;
            yield break;
        }
        yield return type;
    }
}
