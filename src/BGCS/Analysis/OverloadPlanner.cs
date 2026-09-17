namespace BGCS.Analysis;

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
        foreach (BindingParameter candidate in function.Parameters)
        {
            if (ReferenceEquals(candidate, pointer))
                continue;
            string name = candidate.NativeName;
            if (terms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                return candidate.ManagedName;
        }
        return null;
    }
}
