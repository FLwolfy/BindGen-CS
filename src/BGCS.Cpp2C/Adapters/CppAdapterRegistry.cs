using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.Core.Extensibility;

namespace BGCS.Cpp2C.Adapters;

/// <summary>
/// Deterministic registry for third-party type and callable adapters.
/// </summary>
public sealed class CppAdapterRegistry
{
    private readonly object gate = new();
    private readonly List<ICppTypeAdapter> typeAdapters = [];
    private readonly List<ICppCallableAdapter> callableAdapters = [];

    public IReadOnlyList<ICppTypeAdapter> TypeAdapters
    {
        get { lock (gate) return typeAdapters.ToArray(); }
    }

    public IReadOnlyList<ICppCallableAdapter> CallableAdapters
    {
        get { lock (gate) return callableAdapters.ToArray(); }
    }

    public void Register(ICppTypeAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ValidateIdentity(adapter.Name);
        lock (gate)
        {
            if (typeAdapters.Any(value => string.Equals(value.Name, adapter.Name, StringComparison.Ordinal)))
                throw new InvalidOperationException($"A C++ type adapter named '{adapter.Name}' is already registered.");
            typeAdapters.Add(adapter);
            typeAdapters.Sort(Compare);
        }
    }

    public void Register(ICppCallableAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ValidateIdentity(adapter.Name);
        lock (gate)
        {
            if (callableAdapters.Any(value => string.Equals(value.Name, adapter.Name, StringComparison.Ordinal)))
                throw new InvalidOperationException($"A C++ callable adapter named '{adapter.Name}' is already registered.");
            callableAdapters.Add(adapter);
            callableAdapters.Sort(Compare);
        }
    }

    internal bool TryResolve(CppType type, CppTypeAdapterContext context, out CppTypeAdapterPlan? plan)
    {
        ICppTypeAdapter[] snapshot;
        lock (gate) snapshot = typeAdapters.ToArray();
        foreach (ICppTypeAdapter adapter in snapshot)
        {
            if (!adapter.CanAdapt(type, context))
                continue;
            plan = adapter.CreatePlan(type, context);
            ValidatePlan(adapter.Name, plan.AdapterName, plan.CAbiType);
            return true;
        }
        plan = null;
        return false;
    }

    internal bool TryResolve(CppFunction function, CppCallableAdapterContext context, out CppCallableAdapterPlan? plan)
    {
        ICppCallableAdapter[] snapshot;
        lock (gate) snapshot = callableAdapters.ToArray();
        foreach (ICppCallableAdapter adapter in snapshot)
        {
            if (!adapter.CanAdapt(function, context))
                continue;
            plan = adapter.CreatePlan(function, context);
            ValidatePlan(adapter.Name, plan.AdapterName, plan.ExportName);
            return true;
        }
        plan = null;
        return false;
    }

    internal bool TryGetCacheFingerprint(out string fingerprint)
    {
        lock (gate)
        {
            object[] adapters = [.. typeAdapters.Cast<object>(), .. callableAdapters.Cast<object>()];
            if (adapters.Any(adapter => adapter is not ICacheFingerprintProvider))
            {
                fingerprint = string.Empty;
                return false;
            }
            fingerprint = string.Join("\n", adapters.Select(adapter =>
            {
                ICacheFingerprintProvider provider = (ICacheFingerprintProvider)adapter;
                string name = adapter switch
                {
                    ICppTypeAdapter typeAdapter => "type:" + typeAdapter.Name,
                    ICppCallableAdapter callableAdapter => "callable:" + callableAdapter.Name,
                    _ => throw new InvalidOperationException("Unknown C++ adapter registration.")
                };
                return name + "=" + provider.GetCacheFingerprint();
            }).OrderBy(value => value, StringComparer.Ordinal));
            return true;
        }
    }

    private static int Compare(ICppTypeAdapter left, ICppTypeAdapter right)
    {
        int priority = right.Priority.CompareTo(left.Priority);
        return priority != 0 ? priority : StringComparer.Ordinal.Compare(left.Name, right.Name);
    }

    private static int Compare(ICppCallableAdapter left, ICppCallableAdapter right)
    {
        int priority = right.Priority.CompareTo(left.Priority);
        return priority != 0 ? priority : StringComparer.Ordinal.Compare(left.Name, right.Name);
    }

    private static void ValidateIdentity(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Adapter names cannot be empty.", nameof(name));
    }

    private static void ValidatePlan(string registeredName, string planName, string value)
    {
        if (!string.Equals(registeredName, planName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Adapter '{registeredName}' returned a plan owned by '{planName}'.");
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Adapter '{registeredName}' returned an empty ABI value.");
    }
}
