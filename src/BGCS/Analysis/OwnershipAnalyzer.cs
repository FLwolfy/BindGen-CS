namespace BGCS.Analysis;

using BGCS.Core.CSharp;
using BGCS.CppAst.Model.Types;
using BGCS.Intermediate;

/// <summary>
/// Infers conservative ownership and marshalling defaults without inventing transfer semantics not present in native declarations.
/// </summary>
public sealed class OwnershipAnalyzer
{
    private readonly CsCodeGeneratorConfig config;

    public OwnershipAnalyzer(CsCodeGeneratorConfig config)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public MarshallingPlan Analyze(CppType type, Direction direction, MarshallingMapping? mapping = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        MarshallingPlan inferred;
        if (type.IsString(config, out CppPrimitiveKind stringKind))
        {
            BindingStringEncoding encoding = stringKind == CppPrimitiveKind.WChar
                ? BindingStringEncoding.Utf16
                : BindingStringEncoding.Utf8;
            inferred = new(MarshallingStrategy.String, BindingOwnership.Borrowed, encoding,
                RequiresCleanup: false, NullTerminated: true);
        }
        else if (type.IsDelegate(out _))
            inferred = new(MarshallingStrategy.Callback, BindingOwnership.Borrowed);
        else if (type is CppArrayType)
            inferred = new(MarshallingStrategy.Span, BindingOwnership.CallerAllocated);
        else if (type.IsPointer())
            inferred = new(MarshallingStrategy.Pointer,
                direction == Direction.Out ? BindingOwnership.CallerAllocated : BindingOwnership.Borrowed);
        else
            inferred = new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed);
        if (mapping == null)
            return inferred;
        return inferred with
        {
            Strategy = mapping.Strategy ?? inferred.Strategy,
            Ownership = mapping.Ownership ?? inferred.Ownership,
            StringEncoding = mapping.Encoding ?? inferred.StringEncoding,
            LengthParameter = mapping.LengthParameter ?? inferred.LengthParameter,
            CapacityParameter = mapping.CapacityParameter ?? inferred.CapacityParameter,
            WrittenCountParameter = mapping.WrittenCountParameter ?? inferred.WrittenCountParameter,
            RequiresCleanup = mapping.RequiresCleanup ?? inferred.RequiresCleanup,
            CleanupFunction = mapping.CleanupFunction ?? inferred.CleanupFunction,
            NullTerminated = mapping.NullTerminated ?? inferred.NullTerminated,
            AllocatorKind = mapping.AllocatorKind ?? inferred.AllocatorKind,
            AllocatorFunction = mapping.AllocatorFunction ?? inferred.AllocatorFunction,
            CallbackLifetime = mapping.CallbackLifetime ?? inferred.CallbackLifetime,
            CallbackThreading = mapping.CallbackThreading ?? inferred.CallbackThreading,
            UnregisterFunction = mapping.UnregisterFunction ?? inferred.UnregisterFunction,
            AsyncCompletion = mapping.AsyncCompletion ?? inferred.AsyncCompletion,
            CompletionFunction = mapping.CompletionFunction ?? inferred.CompletionFunction
        };
    }
}
