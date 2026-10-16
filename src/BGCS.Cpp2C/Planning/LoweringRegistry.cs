using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Native;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;

namespace BGCS.Cpp2C.Planning;

/// <summary>Freezes neutral lowering order and rejects ambiguous exclusive matches.</summary>
public sealed class LoweringRegistry
{
    private readonly ImmutableArray<TypeRegistration> m_types;
    private readonly ImmutableArray<CallableRegistration> m_callables;
    private sealed record TypeRegistration(
        string Id,
        int Priority,
        ICppTypeLowering Service
    );
    private sealed record CallableRegistration(
        string Id,
        int Priority,
        ICppCallableLowering Service
    );

    /// <summary>Copies one session's lowering services into a deterministic immutable registration snapshot.</summary>
    /// <param name="types">Session-owned type lowerings.</param>
    /// <param name="callables">Session-owned callable lowerings.</param>
    public LoweringRegistry(
        IEnumerable<ICppTypeLowering>? types = null,
        IEnumerable<ICppCallableLowering>? callables = null
    ) {
        m_types = (types ?? []).Select(service => service is null ? throw new ArgumentException("A type lowering service cannot be null.", nameof(types)) : new TypeRegistration(service.id, service.priority, service))
            .OrderByDescending(extension => extension.Priority).ThenBy(extension => extension.Id, StringComparer.Ordinal).ToImmutableArray();
        m_callables = (callables ?? []).Select(service => service is null ? throw new ArgumentException("A callable lowering service cannot be null.", nameof(callables)) : new CallableRegistration(service.id, service.priority, service))
            .OrderByDescending(extension => extension.Priority).ThenBy(extension => extension.Id, StringComparer.Ordinal).ToImmutableArray();
        string[] ids = m_types.Select(extension => extension.Id).Concat(m_callables.Select(extension => extension.Id)).ToArray();
        foreach (string id in ids) ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new InvalidOperationException("A lowering service identity is registered more than once.");
    }

    /// <summary>Matches the highest-priority type tier and rejects competing exclusive claims.</summary>
    /// <param name="type">The neutral native type view.</param>
    /// <param name="context">Detached target and source facts.</param>
    /// <param name="cancellationToken">Cancels cooperative planning.</param>
    /// <returns>A complete representation plan, or null when no extension claims the type.</returns>
    public CppTypeLoweringPlan? LowerType(
        NativeTypeView type,
        LoweringContext context,
        CancellationToken cancellationToken
    ) {
        foreach (var tier in m_types.GroupBy(extension => extension.Priority))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (TypeRegistration extension in tier)
                if (extension.Service.id != extension.Id || extension.Service.priority != extension.Priority) throw new InvalidOperationException("A type lowering changed its frozen registration identity.");
            TypeRegistration[] matches = tier.Where(extension => extension.Service.CanLower(type, context)).ToArray();
            foreach (TypeRegistration extension in tier)
                if (extension.Service.id != extension.Id || extension.Service.priority != extension.Priority) throw new InvalidOperationException("A type lowering changed its frozen registration identity during matching.");
            if (matches.Length > 1) throw new InvalidOperationException("Exclusive type lowerings conflict: " + string.Join(", ", matches.Select(extension => extension.Id)));
            if (matches.Length == 1)
            {
                CppTypeLoweringPlan result = matches[0].Service.Lower(type, context, cancellationToken) ?? throw new InvalidOperationException("A type lowering returned no plan.");
                if (matches[0].Service.id != matches[0].Id || matches[0].Service.priority != matches[0].Priority) throw new InvalidOperationException("A type lowering changed its frozen registration identity during planning.");
                return result;
            }
        }
        return null;
    }

    /// <summary>Matches the highest-priority callable tier and rejects competing exclusive claims.</summary>
    /// <param name="callable">The neutral callable declaration.</param>
    /// <param name="context">Detached target and source facts.</param>
    /// <param name="cancellationToken">Cancels cooperative planning.</param>
    /// <returns>A complete export plan, or null when no extension claims the callable.</returns>
    public CppCallableLoweringPlan? LowerCallable(
        NativeDeclaration callable,
        LoweringContext context,
        CancellationToken cancellationToken
    ) {
        foreach (var tier in m_callables.GroupBy(extension => extension.Priority))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (CallableRegistration extension in tier)
                if (extension.Service.id != extension.Id || extension.Service.priority != extension.Priority) throw new InvalidOperationException("A callable lowering changed its frozen registration identity.");
            CallableRegistration[] matches = tier.Where(extension => extension.Service.CanLower(callable, context)).ToArray();
            foreach (CallableRegistration extension in tier)
                if (extension.Service.id != extension.Id || extension.Service.priority != extension.Priority) throw new InvalidOperationException("A callable lowering changed its frozen registration identity during matching.");
            if (matches.Length > 1) throw new InvalidOperationException("Exclusive callable lowerings conflict: " + string.Join(", ", matches.Select(extension => extension.Id)));
            if (matches.Length == 1)
            {
                CppCallableLoweringPlan result = matches[0].Service.Lower(callable, context, cancellationToken) ?? throw new InvalidOperationException("A callable lowering returned no plan.");
                if (matches[0].Service.id != matches[0].Id || matches[0].Service.priority != matches[0].Priority) throw new InvalidOperationException("A callable lowering changed its frozen registration identity during planning.");
                return result;
            }
        }
        return null;
    }
}
