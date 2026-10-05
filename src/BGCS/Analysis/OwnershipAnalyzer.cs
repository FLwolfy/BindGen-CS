using System;
using BGCS.Configuration;
using BGCS.Conversion;

namespace BGCS.Analysis;

using BGCS.CppAst.Model.Types;
using BGCS.CSharp;
using BGCS.Intermediate;

/// <summary>
/// Infers conservative ownership and marshalling defaults without inventing transfer semantics not present in native declarations.
/// </summary>
public sealed class OwnershipAnalyzer
{
    private readonly CsCodeGeneratorConfig m_config;

    /// <summary>
    /// Creates an ownership analyzer using the configured native mappings.
    /// </summary>
    /// <param name="config">Configuration borrowed for this analysis attempt.</param>
    public OwnershipAnalyzer(CsCodeGeneratorConfig config)
    {
        this.m_config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Infers conservative marshalling facts and applies explicitly declared ownership policy.
    /// </summary>
    /// <param name="type">Native type from a live compilation.</param>
    /// <param name="direction">Direction of data transfer for this use.</param>
    /// <param name="mapping">Optional explicit overrides; unspecified facts retain the conservative inference.</param>
    /// <returns>An AST-independent plan; native transfer of ownership is never inferred without an explicit mapping.</returns>
    public MarshallingPlan Analyze(
        CppType type,
        Direction direction,
        MarshallingMapping? mapping = null
    ) {
        ArgumentNullException.ThrowIfNull(type);
        MarshallingPlan inferred;
        if (type.IsString(this.m_config, out CppPrimitiveKind stringKind))
        {
            BindingStringEncoding encoding = stringKind == CppPrimitiveKind.WChar ? BindingStringEncoding.Utf16 : BindingStringEncoding.Utf8;
            inferred = new(MarshallingStrategy.String, BindingOwnership.Borrowed, encoding, requiresCleanup: false, nullTerminated: true);
        }
        else if (type.IsDelegate(out _))
            inferred = new(MarshallingStrategy.Callback, BindingOwnership.Borrowed);
        else if (type is CppArrayType)
            inferred = new(MarshallingStrategy.Span, BindingOwnership.CallerAllocated);
        else if (type.IsPointer())
            inferred = new(MarshallingStrategy.Pointer, direction == Direction.Out ? BindingOwnership.CallerAllocated : BindingOwnership.Borrowed);
        else
            inferred = new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed);
        if (mapping == null)
            return inferred;
        return inferred with
        {
            strategy = mapping.strategy ?? inferred.strategy,
            ownership = mapping.ownership ?? inferred.ownership,
            stringEncoding = mapping.encoding ?? inferred.stringEncoding,
            lengthParameter = mapping.lengthParameter ?? inferred.lengthParameter,
            capacityParameter = mapping.capacityParameter ?? inferred.capacityParameter,
            writtenCountParameter = mapping.writtenCountParameter ?? inferred.writtenCountParameter,
            requiresCleanup = mapping.requiresCleanup ?? inferred.requiresCleanup,
            cleanupFunction = mapping.cleanupFunction ?? inferred.cleanupFunction,
            nullTerminated = mapping.nullTerminated ?? inferred.nullTerminated,
            allocatorKind = mapping.allocatorKind ?? inferred.allocatorKind,
            allocatorFunction = mapping.allocatorFunction ?? inferred.allocatorFunction,
            callbackLifetime = mapping.callbackLifetime ?? inferred.callbackLifetime,
            callbackThreading = mapping.callbackThreading ?? inferred.callbackThreading,
            unregisterFunction = mapping.unregisterFunction ?? inferred.unregisterFunction,
            asyncCompletion = mapping.asyncCompletion ?? inferred.asyncCompletion,
            completionFunction = mapping.completionFunction ?? inferred.completionFunction
        };
    }
}
