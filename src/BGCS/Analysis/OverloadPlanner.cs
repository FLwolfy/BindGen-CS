using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Analysis;

using System.Text.RegularExpressions;
using BGCS.Intermediate;

/// <summary>
/// Detects count/capacity relationships and enriches function marshalling plans before emission.
/// </summary>
internal sealed class OverloadPlanner
{
    public void Plan(BindingFunctionBuilder function)
    {
        ArgumentNullException.ThrowIfNull(function);
        for (int i = 0; i < function.parameters.Count; i++)
        {
            BindingParameter parameter = function.parameters[i];
            if (parameter.marshalling.strategy is not (MarshallingStrategy.Pointer or MarshallingStrategy.Span))
                continue;
            string? length = parameter.marshalling.lengthParameter ?? FindRelated(function, parameter, "count", "length", "size");
            string? capacity = parameter.marshalling.capacityParameter ?? FindRelated(function, parameter, "capacity", "max", "limit");
            string? written = parameter.marshalling.writtenCountParameter ?? FindRelated(function, parameter, "written", "actual", "output_count");
            if (length == null && capacity == null && written == null)
                continue;
            function.parameters[i] = parameter with
            {
                marshalling = parameter.marshalling with
                {
                    lengthParameter = length,
                    capacityParameter = capacity,
                    writtenCountParameter = written
                }
            };
        }
    }

    private static string? FindRelated(
        BindingFunctionBuilder function,
        BindingParameter pointer,
        params string[] terms
    ) {
        BindingParameter[] candidates = function.parameters.Where(candidate => !ReferenceEquals(candidate, pointer) && candidate.type.pointerDepth == 0 && !candidate.type.managedName.Contains('*', StringComparison.Ordinal) && IsIntegralCountType(candidate.type.managedName) && terms.Any(term => candidate.nativeName.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (candidates.Length == 0)
            return null;
        HashSet<string> pointerTokens = GetSemanticTokens(pointer.nativeName, []);
        BindingParameter? best = null;
        int bestScore = 0;
        foreach (BindingParameter candidate in candidates)
        {
            HashSet<string> candidateTokens = GetSemanticTokens(candidate.nativeName, terms);
            int score = pointerTokens.Count(candidateTokens.Contains);
            if (score <= bestScore)
                continue;
            best = candidate;
            bestScore = score;
        }

        if (best != null)
            return best.managedName;
        int pointerCount = function.parameters.Count(parameter => parameter.marshalling.strategy is MarshallingStrategy.Pointer or MarshallingStrategy.Span);
        if (pointerCount != 1 || candidates.Length != 1)
            return null;
        int pointerIndex = function.parameters.IndexOf(pointer);
        int candidateIndex = function.parameters.IndexOf(candidates[0]);
        return Math.Abs(pointerIndex - candidateIndex) == 1 ? candidates[0].managedName : null;
    }

    private static bool IsIntegralCountType(string managedType) => managedType is "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "nint" or "nuint";
    private static HashSet<string> GetSemanticTokens(
        string name,
        IReadOnlyCollection<string> roleTerms
    ) {
        string[] parts = Regex.Split(name, "(?<=[a-z0-9])(?=[A-Z])|_+");
        HashSet<string> ignored = new(StringComparer.OrdinalIgnoreCase)
        {
            "p",
            "pp",
            "ptr",
            "pointer",
            "in",
            "out",
            "actual",
            "written",
            "max",
            "limit"
        };
        foreach (string term in roleTerms)
            ignored.Add(term);
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string part in parts)
        {
            string token = part.ToLowerInvariant();
            if (token.Length > 3 && token.EndsWith('s'))
                token = token[..^1];
            if (token.Length > 0 && !ignored.Contains(token))
                result.Add(token);
        }

        return result;
    }
}
