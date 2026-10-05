using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Intermediate;

internal static class BindingCollection
{
    internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.ToArray());
    }
}
