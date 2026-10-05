using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using BGCS.Core.Extensibility;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;

namespace BGCS.Cpp2C.Lowering;

/// <summary>Thread-safe deterministic registry for the complete C++ lowering pipeline.</summary>
public sealed class CppLoweringRegistry
{
    private readonly object m_gate = new();
    private IReadOnlyList<ICppTypeLowering> m_typeLowerings = [];
    private IReadOnlyList<ICppCallableLowering> m_callableLowerings = [];
    private IReadOnlyList<ICppArtifactContributor> m_artifactContributors = [];
    private readonly SortedSet<string> m_unsafeBypasses = new(StringComparer.Ordinal);
    /// <summary>
    /// Gets the current immutable type-lowering snapshot in priority and ordinal name order.
    /// Previously returned snapshots remain unchanged after registration.
    /// </summary>
    public IReadOnlyList<ICppTypeLowering> typeLowerings => Volatile.Read(ref m_typeLowerings);

    /// <summary>
    /// Gets the current immutable callable-lowering snapshot in priority and ordinal name order.
    /// </summary>
    public IReadOnlyList<ICppCallableLowering> callableLowerings => Volatile.Read(ref m_callableLowerings);

    /// <summary>
    /// Gets the current immutable artifact-contributor snapshot in priority and ordinal name order.
    /// </summary>
    public IReadOnlyList<ICppArtifactContributor> artifactContributors => Volatile.Read(ref m_artifactContributors);

    /// <summary>
    /// Gets an ordinally sorted snapshot of unsafe extensions used during the current generation.
    /// </summary>
    public IReadOnlyList<string> unsafeBypasses
    {
        get
        {
            lock (this.m_gate)
                return this.m_unsafeBypasses.ToArray();
        }
    }

    /// <summary>
    /// Publishes a type lowering in a new immutable registry snapshot.
    /// </summary>
    /// <param name="lowering">Extension retained by the registry until the registry is released.</param>
    /// <exception cref="ArgumentNullException">The extension is null.</exception>
    /// <exception cref="ArgumentException">The extension name is empty.</exception>
    /// <exception cref="InvalidOperationException">Its name is already registered in this category.</exception>
    public void Register(ICppTypeLowering lowering)
    {
        lock (m_gate)
            Volatile.Write(ref m_typeLowerings, CreateRegistration(lowering, m_typeLowerings));
    }

    /// <summary>
    /// Publishes a callable lowering in a new immutable registry snapshot.
    /// </summary>
    /// <param name="lowering">Extension retained by the registry until the registry is released.</param>
    /// <exception cref="ArgumentNullException">The extension is null.</exception>
    /// <exception cref="ArgumentException">The extension name is empty.</exception>
    /// <exception cref="InvalidOperationException">Its name is already registered in this category.</exception>
    public void Register(ICppCallableLowering lowering)
    {
        lock (m_gate)
            Volatile.Write(ref m_callableLowerings, CreateRegistration(lowering, m_callableLowerings));
    }

    /// <summary>
    /// Publishes an artifact contributor in a new immutable registry snapshot.
    /// </summary>
    /// <param name="contributor">Extension retained by the registry until the registry is released.</param>
    /// <exception cref="ArgumentNullException">The extension is null.</exception>
    /// <exception cref="ArgumentException">The extension name is empty.</exception>
    /// <exception cref="InvalidOperationException">Its name is already registered in this category.</exception>
    public void Register(ICppArtifactContributor contributor)
    {
        lock (m_gate)
            Volatile.Write(ref m_artifactContributors, CreateRegistration(contributor, m_artifactContributors));
    }
    internal void BeginGeneration()
    {
        lock (this.m_gate)
            this.m_unsafeBypasses.Clear();
    }

    internal bool TryResolve(
        CppType type,
        CppTypeLoweringContext context,
        out CppTypeLoweringPlan? plan
    ) {
        foreach (ICppTypeLowering lowering in typeLowerings)
        {
            if (!lowering.CanLower(type, context))
                continue;
            plan = lowering.CreatePlan(type, context);
            ValidatePlan(lowering.name, plan, context.configuration.loweringSafetyPolicy);
            RecordBypass(lowering.name, plan.safety);
            return true;
        }

        plan = null;
        return false;
    }

    internal bool TryResolve(
        CppFunction function,
        CppCallableLoweringContext context,
        out CppCallableLoweringPlan? plan
    ) {
        foreach (ICppCallableLowering lowering in callableLowerings)
        {
            if (!lowering.CanLower(function, context))
                continue;
            plan = lowering.CreatePlan(function, context);
            ValidateIdentity(lowering.name, plan.loweringName);
            ValidateSafety(lowering.name, plan.safety, context.configuration.loweringSafetyPolicy);
            RecordBypass(lowering.name, plan.safety);
            if (string.IsNullOrWhiteSpace(plan.exportName))
                throw new InvalidOperationException($"Lowering '{lowering.name}' returned an empty export name.");
            ValidateTemplate(plan.invocationExpression, "{invocation}", lowering.name);
            return true;
        }

        plan = null;
        return false;
    }

    internal IReadOnlyList<CppGeneratedArtifact> CollectArtifacts(CppArtifactContext context)
    {
        List<CppGeneratedArtifact> result = [];
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (ICppArtifactContributor contributor in artifactContributors)
        {
            IReadOnlyList<CppGeneratedArtifact> artifacts = contributor.Contribute(context)
                ?? throw new InvalidOperationException($"Contributor '{contributor.name}' returned a null artifact sequence.");
            foreach (CppGeneratedArtifact artifact in artifacts)
            {
                ValidateSafety(contributor.name, artifact.safety, context.configuration.loweringSafetyPolicy);
                RecordBypass(contributor.name, artifact.safety);
                ValidateRelativePath(artifact.relativePath, contributor.name);
                if (!paths.Add(artifact.relativePath))
                    throw new InvalidOperationException($"Lowering contributors produced duplicate artifact '{artifact.relativePath}'.");
                result.Add(artifact);
            }
        }

        return result.OrderBy(value => value.relativePath, StringComparer.Ordinal).ToArray();
    }

    internal bool TryGetCacheFingerprint(out string fingerprint)
    {
        object[] extensions;
        lock (this.m_gate)
        {
            extensions = [.. this.m_typeLowerings.Cast<object>(), .. this.m_callableLowerings.Cast<object>(), .. this.m_artifactContributors.Cast<object>()];
        }
        if (extensions.Any(extension => extension is not ICacheFingerprintProvider))
        {
            fingerprint = string.Empty;
            return false;
        }

        fingerprint = string.Join("\n", extensions.Select(extension =>
            {
                string identity = extension switch
                {
                    ICppTypeLowering value => "type:" + value.name,
                    ICppCallableLowering value => "callable:" + value.name,
                    ICppArtifactContributor value => "artifact:" + value.name,
                    _ => throw new InvalidOperationException("Unknown C++ lowering registration.")
                };
                return identity + "=" + ((ICacheFingerprintProvider)extension).GetCacheFingerprint();
            }).OrderBy(value => value, StringComparer.Ordinal));
        return true;
    }

    private static IReadOnlyList<T> CreateRegistration<T>(
        T extension,
        IReadOnlyList<T> collection
    )
        where T : class
    {
        ArgumentNullException.ThrowIfNull(extension);
        string name = extension switch
        {
            ICppTypeLowering value => value.name,
            ICppCallableLowering value => value.name,
            ICppArtifactContributor value => value.name,
            _ => throw new ArgumentException("Unsupported lowering extension type.", nameof(extension))
        };
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Lowering names cannot be empty.", nameof(extension));
        if (collection.Any(value => string.Equals(GetName(value), name, StringComparison.Ordinal)))
            throw new InvalidOperationException($"A C++ lowering named '{name}' is already registered for {typeof(T).Name}.");
        T[] snapshot = [.. collection, extension];
        Array.Sort(snapshot, (
            left,
            right
        ) => {
            int order = GetPriority(right).CompareTo(GetPriority(left));
            return order != 0 ? order : StringComparer.Ordinal.Compare(GetName(left), GetName(right));
        });
        return Array.AsReadOnly(snapshot);
    }

    private void RecordBypass(
        string name,
        CppLoweringSafety safety
    ) {
        if (safety != CppLoweringSafety.Unsafe)
            return;
        lock (this.m_gate)
            this.m_unsafeBypasses.Add(name);
    }

    private static string GetName<T>(T value) => value switch
    {
        ICppTypeLowering lowering => lowering.name,
        ICppCallableLowering lowering => lowering.name,
        ICppArtifactContributor contributor => contributor.name,
        _ => string.Empty
    };
    private static int GetPriority<T>(T value) => value switch
    {
        ICppTypeLowering lowering => lowering.priority,
        ICppCallableLowering lowering => lowering.priority,
        ICppArtifactContributor contributor => contributor.priority,
        _ => 0
    };
    private static void ValidatePlan(
        string registeredName,
        CppTypeLoweringPlan plan,
        CppLoweringSafetyPolicy policy
    ) {
        ValidateIdentity(registeredName, plan.loweringName);
        if (string.IsNullOrWhiteSpace(plan.cAbiType))
            throw new InvalidOperationException($"Lowering '{registeredName}' returned an empty C ABI type.");
        ValidateSafety(registeredName, plan.safety, policy);
        HashSet<string> suffixes = new(StringComparer.Ordinal);
        foreach (CppAbiParameter parameter in plan.abiParameters)
        {
            if (!Regex.IsMatch(parameter.nameSuffix, "^[_A-Za-z0-9]*$", RegexOptions.CultureInvariant) || string.IsNullOrWhiteSpace(parameter.cAbiType) || !suffixes.Add(parameter.nameSuffix))
                throw new InvalidOperationException($"Lowering '{registeredName}' returned invalid or duplicate ABI parameter metadata.");
        }

        ValidateTemplate(plan.parameterToCppExpression, "{value}", registeredName);
        ValidateTemplate(plan.returnToCExpression, "{value}", registeredName);
        if (plan.requiresCleanup && string.IsNullOrWhiteSpace(plan.cleanupFunction))
            throw new InvalidOperationException($"Lowering '{registeredName}' owns storage but did not identify a cleanup function.");
    }

    private static void ValidateIdentity(
        string registeredName,
        string planName
    ) {
        if (!string.Equals(registeredName, planName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Lowering '{registeredName}' returned a plan owned by '{planName}'.");
    }

    private static void ValidateSafety(
        string name,
        CppLoweringSafety safety,
        CppLoweringSafetyPolicy policy
    ) {
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

    private static void ValidateTemplate(
        string? template,
        string requiredPlaceholder,
        string name
    ) {
        if (template != null && !template.Contains(requiredPlaceholder, StringComparison.Ordinal))
            throw new InvalidOperationException($"Lowering '{name}' expression must contain '{requiredPlaceholder}'.");
    }

    private static void ValidateRelativePath(
        string path,
        string contributor
    ) {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            throw new InvalidOperationException($"Artifact contributor '{contributor}' returned a non-relative path.");
        string normalized = path.Replace('\\', '/');
        if (normalized.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidOperationException($"Artifact contributor '{contributor}' returned unsafe path '{path}'.");
    }
}
