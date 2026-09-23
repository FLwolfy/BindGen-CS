using System.Text.RegularExpressions;
using BGCS.Core.Extensibility;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;

namespace BGCS.Cpp2C.Lowering;

/// <summary>Thread-safe deterministic registry for the complete C++ lowering pipeline.</summary>
public sealed class CppLoweringRegistry
{
    private readonly object gate = new();
    private readonly List<ICppTypeLowering> typeLowerings = [];
    private readonly List<ICppCallableLowering> callableLowerings = [];
    private readonly List<ICppArtifactContributor> artifactContributors = [];
    private readonly SortedSet<string> unsafeBypasses = new(StringComparer.Ordinal);

    public IReadOnlyList<ICppTypeLowering> TypeLowerings { get { lock (gate) return typeLowerings.ToArray(); } }
    public IReadOnlyList<ICppCallableLowering> CallableLowerings { get { lock (gate) return callableLowerings.ToArray(); } }
    public IReadOnlyList<ICppArtifactContributor> ArtifactContributors { get { lock (gate) return artifactContributors.ToArray(); } }
    public IReadOnlyList<string> UnsafeBypasses { get { lock (gate) return unsafeBypasses.ToArray(); } }

    public void Register(ICppTypeLowering lowering) => RegisterCore(lowering, typeLowerings);
    public void Register(ICppCallableLowering lowering) => RegisterCore(lowering, callableLowerings);
    public void Register(ICppArtifactContributor contributor) => RegisterCore(contributor, artifactContributors);

    internal void BeginGeneration()
    {
        lock (gate)
            unsafeBypasses.Clear();
    }

    internal bool TryResolve(CppType type, CppTypeLoweringContext context, out CppTypeLoweringPlan? plan)
    {
        foreach (ICppTypeLowering lowering in Snapshot(typeLowerings))
        {
            if (!lowering.CanLower(type, context))
                continue;
            plan = lowering.CreatePlan(type, context);
            ValidatePlan(lowering.Name, plan, context.Configuration.LoweringSafetyPolicy);
            RecordBypass(lowering.Name, plan.Safety);
            return true;
        }
        plan = null;
        return false;
    }

    internal bool TryResolve(CppFunction function, CppCallableLoweringContext context, out CppCallableLoweringPlan? plan)
    {
        foreach (ICppCallableLowering lowering in Snapshot(callableLowerings))
        {
            if (!lowering.CanLower(function, context))
                continue;
            plan = lowering.CreatePlan(function, context);
            ValidateIdentity(lowering.Name, plan.LoweringName);
            ValidateSafety(lowering.Name, plan.Safety, context.Configuration.LoweringSafetyPolicy);
            RecordBypass(lowering.Name, plan.Safety);
            if (string.IsNullOrWhiteSpace(plan.ExportName))
                throw new InvalidOperationException($"Lowering '{lowering.Name}' returned an empty export name.");
            ValidateTemplate(plan.InvocationExpression, "{invocation}", lowering.Name);
            return true;
        }
        plan = null;
        return false;
    }

    internal IReadOnlyList<CppGeneratedArtifact> CollectArtifacts(CppArtifactContext context)
    {
        List<CppGeneratedArtifact> result = [];
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (ICppArtifactContributor contributor in Snapshot(artifactContributors))
        {
            foreach (CppGeneratedArtifact artifact in contributor.Contribute(context) ?? [])
            {
                ValidateSafety(contributor.Name, artifact.Safety, context.Configuration.LoweringSafetyPolicy);
                RecordBypass(contributor.Name, artifact.Safety);
                ValidateRelativePath(artifact.RelativePath, contributor.Name);
                if (!paths.Add(artifact.RelativePath))
                    throw new InvalidOperationException($"Lowering contributors produced duplicate artifact '{artifact.RelativePath}'.");
                result.Add(artifact);
            }
        }
        return result.OrderBy(value => value.RelativePath, StringComparer.Ordinal).ToArray();
    }

    internal bool TryGetCacheFingerprint(out string fingerprint)
    {
        lock (gate)
        {
            object[] extensions = [.. typeLowerings.Cast<object>(), .. callableLowerings.Cast<object>(), .. artifactContributors.Cast<object>()];
            if (extensions.Any(extension => extension is not ICacheFingerprintProvider))
            {
                fingerprint = string.Empty;
                return false;
            }
            fingerprint = string.Join("\n", extensions.Select(extension =>
            {
                string identity = extension switch
                {
                    ICppTypeLowering value => "type:" + value.Name,
                    ICppCallableLowering value => "callable:" + value.Name,
                    ICppArtifactContributor value => "artifact:" + value.Name,
                    _ => throw new InvalidOperationException("Unknown C++ lowering registration.")
                };
                return identity + "=" + ((ICacheFingerprintProvider)extension).GetCacheFingerprint();
            }).OrderBy(value => value, StringComparer.Ordinal));
            return true;
        }
    }

    private void RegisterCore<T>(T extension, List<T> collection) where T : class
    {
        ArgumentNullException.ThrowIfNull(extension);
        string name = extension switch
        {
            ICppTypeLowering value => value.Name,
            ICppCallableLowering value => value.Name,
            ICppArtifactContributor value => value.Name,
            _ => throw new ArgumentException("Unsupported lowering extension type.", nameof(extension))
        };
        int priority = extension switch
        {
            ICppTypeLowering value => value.Priority,
            ICppCallableLowering value => value.Priority,
            ICppArtifactContributor value => value.Priority,
            _ => 0
        };
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Lowering names cannot be empty.", nameof(extension));
        lock (gate)
        {
            if (collection.Any(value => string.Equals(GetName(value), name, StringComparison.Ordinal)))
                throw new InvalidOperationException($"A C++ lowering named '{name}' is already registered for {typeof(T).Name}.");
            collection.Add(extension);
            collection.Sort((left, right) =>
            {
                int order = GetPriority(right).CompareTo(GetPriority(left));
                return order != 0 ? order : StringComparer.Ordinal.Compare(GetName(left), GetName(right));
            });
        }
    }

    private T[] Snapshot<T>(List<T> collection)
    {
        lock (gate) return collection.ToArray();
    }

    private void RecordBypass(string name, CppLoweringSafety safety)
    {
        if (safety != CppLoweringSafety.Unsafe)
            return;
        lock (gate)
            unsafeBypasses.Add(name);
    }

    private static string GetName<T>(T value) => value switch
    {
        ICppTypeLowering lowering => lowering.Name,
        ICppCallableLowering lowering => lowering.Name,
        ICppArtifactContributor contributor => contributor.Name,
        _ => string.Empty
    };

    private static int GetPriority<T>(T value) => value switch
    {
        ICppTypeLowering lowering => lowering.Priority,
        ICppCallableLowering lowering => lowering.Priority,
        ICppArtifactContributor contributor => contributor.Priority,
        _ => 0
    };

    private static void ValidatePlan(string registeredName, CppTypeLoweringPlan plan, CppLoweringSafetyPolicy policy)
    {
        ValidateIdentity(registeredName, plan.LoweringName);
        if (string.IsNullOrWhiteSpace(plan.CAbiType))
            throw new InvalidOperationException($"Lowering '{registeredName}' returned an empty C ABI type.");
        ValidateSafety(registeredName, plan.Safety, policy);
        HashSet<string> suffixes = new(StringComparer.Ordinal);
        foreach (CppAbiParameter parameter in plan.AbiParameters)
        {
            if (!Regex.IsMatch(parameter.NameSuffix, "^[_A-Za-z0-9]*$", RegexOptions.CultureInvariant) ||
                string.IsNullOrWhiteSpace(parameter.CAbiType) || !suffixes.Add(parameter.NameSuffix))
                throw new InvalidOperationException($"Lowering '{registeredName}' returned invalid or duplicate ABI parameter metadata.");
        }
        ValidateTemplate(plan.ParameterToCppExpression, "{value}", registeredName);
        ValidateTemplate(plan.ReturnToCExpression, "{value}", registeredName);
        if (plan.RequiresCleanup && string.IsNullOrWhiteSpace(plan.CleanupFunction))
            throw new InvalidOperationException($"Lowering '{registeredName}' owns storage but did not identify a cleanup function.");
    }

    private static void ValidateIdentity(string registeredName, string planName)
    {
        if (!string.Equals(registeredName, planName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Lowering '{registeredName}' returned a plan owned by '{planName}'.");
    }

    private static void ValidateSafety(string name, CppLoweringSafety safety, CppLoweringSafetyPolicy policy)
    {
        bool accepted = safety switch
        {
            CppLoweringSafety.Verified => true,
            CppLoweringSafety.UserAsserted => policy is CppLoweringSafetyPolicy.AllowUserAsserted or CppLoweringSafetyPolicy.AllowUnsafe,
            CppLoweringSafety.Unsafe => policy == CppLoweringSafetyPolicy.AllowUnsafe,
            _ => false
        };
        if (!accepted)
            throw new InvalidOperationException($"Lowering '{name}' has safety level {safety}, rejected by policy {policy}. Explicitly opt in only after reviewing its ABI and lifetime contract.");
    }

    private static void ValidateTemplate(string? template, string requiredPlaceholder, string name)
    {
        if (template != null && !template.Contains(requiredPlaceholder, StringComparison.Ordinal))
            throw new InvalidOperationException($"Lowering '{name}' expression must contain '{requiredPlaceholder}'.");
    }

    private static void ValidateRelativePath(string path, string contributor)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            throw new InvalidOperationException($"Artifact contributor '{contributor}' returned a non-relative path.");
        string normalized = path.Replace('\\', '/');
        if (normalized.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidOperationException($"Artifact contributor '{contributor}' returned unsafe path '{path}'.");
    }
}
