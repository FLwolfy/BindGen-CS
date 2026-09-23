namespace BGCS.Analysis;

using System.Text.RegularExpressions;
using BGCS.Intermediate;

/// <summary>
/// Detects count/capacity relationships and enriches function marshalling plans before emission.
/// </summary>
public sealed class OverloadPlanner
{
    public void Plan(BindingFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        for (int i = 0; i < function.Parameters.Count; i++)
        {
            BindingParameter parameter = function.Parameters[i];
            if (parameter.Marshalling.Strategy is not (MarshallingStrategy.Pointer or MarshallingStrategy.Span))
                continue;
            string? length = parameter.Marshalling.LengthParameter ?? FindRelated(function, parameter, "count", "length", "size");
            string? capacity = parameter.Marshalling.CapacityParameter ?? FindRelated(function, parameter, "capacity", "max", "limit");
            string? written = parameter.Marshalling.WrittenCountParameter ?? FindRelated(function, parameter, "written", "actual", "output_count");
            if (length == null && capacity == null && written == null)
                continue;
            function.Parameters[i] = parameter with
            {
                Marshalling = parameter.Marshalling with
                {
                    LengthParameter = length,
                    CapacityParameter = capacity,
                    WrittenCountParameter = written
                }
            };
        }
    }

    private static string? FindRelated(BindingFunction function, BindingParameter pointer, params string[] terms)
    {
        BindingParameter[] candidates = function.Parameters
            .Where(candidate => !ReferenceEquals(candidate, pointer) &&
                candidate.Type.PointerDepth == 0 &&
                !candidate.Type.ManagedName.Contains('*', StringComparison.Ordinal) &&
                IsIntegralCountType(candidate.Type.ManagedName) &&
                terms.Any(term => candidate.NativeName.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (candidates.Length == 0)
            return null;

        HashSet<string> pointerTokens = GetSemanticTokens(pointer.NativeName, []);
        BindingParameter? best = null;
        int bestScore = 0;
        foreach (BindingParameter candidate in candidates)
        {
            HashSet<string> candidateTokens = GetSemanticTokens(candidate.NativeName, terms);
            int score = pointerTokens.Count(candidateTokens.Contains);
            if (score <= bestScore)
                continue;
            best = candidate;
            bestScore = score;
        }
        if (best != null)
            return best.ManagedName;

        int pointerCount = function.Parameters.Count(parameter =>
            parameter.Marshalling.Strategy is MarshallingStrategy.Pointer or MarshallingStrategy.Span);
        if (pointerCount != 1 || candidates.Length != 1)
            return null;
        int pointerIndex = function.Parameters.IndexOf(pointer);
        int candidateIndex = function.Parameters.IndexOf(candidates[0]);
        return Math.Abs(pointerIndex - candidateIndex) == 1 ? candidates[0].ManagedName : null;
    }

    private static bool IsIntegralCountType(string managedType) => managedType is
        "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "nint" or "nuint";

    private static HashSet<string> GetSemanticTokens(string name, IReadOnlyCollection<string> roleTerms)
    {
        string[] parts = Regex.Split(name, "(?<=[a-z0-9])(?=[A-Z])|_+");
        HashSet<string> ignored = new(StringComparer.OrdinalIgnoreCase)
        {
            "p", "pp", "ptr", "pointer", "in", "out", "actual", "written", "max", "limit"
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
