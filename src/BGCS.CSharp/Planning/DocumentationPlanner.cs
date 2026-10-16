using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;

namespace BGCS.CSharp.Planning;

internal static class DocumentationPlanner
{
    internal static CSharpCallableDocumentationPlan? Plan(InteropFunction function)
    {
        if (function.documentationContent is not SourceDocumentation source) return null;
        if (source.parameters.IsDefault || source.typeParameters.IsDefault)
            throw new ArgumentException("Documentation section collections must be initialized.");
        string?[] parameters = new string?[function.parameters.Length];
        List<string> notes = [];
        foreach (SourceParameterDocumentation parameter in source.parameters)
        {
            int[] matches = function.parameters.Select((
                value,
                index
            ) => (value, index))
                .Where(item => (item.value.sourceName ?? item.value.name) == parameter.name && !string.IsNullOrWhiteSpace(parameter.name))
                .Select(item => item.index).ToArray();
            if (matches.Length == 1)
            {
                int index = matches[0];
                parameters[index] = parameters[index] is null ? parameter.text : parameters[index] + Environment.NewLine + parameter.text;
            }
            else notes.Add("Native parameter " + parameter.name + ": " + parameter.text);
        }
        foreach (SourceTypeParameterDocumentation parameter in source.typeParameters)
            notes.Add("Native template parameter " + parameter.name + ": " + parameter.text);
        return new(source.summary, parameters.ToImmutableArray(), source.returns, notes.ToImmutableArray(), function.error is not null);
    }
}
