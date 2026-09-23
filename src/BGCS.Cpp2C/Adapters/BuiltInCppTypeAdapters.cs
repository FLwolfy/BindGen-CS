using BGCS.CppAst.Model.Types;
using BGCS.Core.Extensibility;
using BGCS.Intermediate;

namespace BGCS.Cpp2C.Adapters;

internal static class BuiltInCppTypeAdapters
{
    internal static IReadOnlyList<ICppTypeAdapter> All { get; } =
    [
        new Utf8StringAdapter(),
        new SharedOwnerAdapter(),
        new UniqueOwnerAdapter(),
        new SpanAdapter(),
        new VectorAdapter(),
        new OptionalAdapter(),
        new ArrayAdapter(),
        new MapAdapter(),
        new SetAdapter(),
        new VariantAdapter(),
        new ExpectedAdapter(),
        new PathAdapter(),
        new ChronoDurationAdapter(),
        new ChronoTimePointAdapter()
    ];

    private abstract class BuiltInAdapter(string name, CppTypeAdapterKind kind) : ICppTypeAdapter, ICacheFingerprintProvider
    {
        public string Name { get; } = name;
        public int Priority => 0;
        protected CppTypeAdapterKind Kind { get; } = kind;
        public abstract bool CanAdapt(CppType type, CppTypeAdapterContext context);
        public abstract CppTypeAdapterPlan CreatePlan(CppType type, CppTypeAdapterContext context);
        public string GetCacheFingerprint() => $"{CppAdapterContract.CurrentVersion}:{Name}";

        protected CppTypeAdapterPlan ElementPointer(CppType type, CppTypeAdapterContext context,
            MarshallingStrategy strategy, BindingOwnership ownership, bool cleanup = false)
        {
            if (!context.Configuration.TryGetTemplateElementType(type, out CppType? elementType))
                throw new NotSupportedException($"Unable to resolve adapter element type '{type}'.");
            return new(Name, Kind, context.Configuration.GetCType(elementType!) + "*", strategy, ownership, cleanup);
        }
    }

    private sealed class Utf8StringAdapter : BuiltInAdapter
    {
        public Utf8StringAdapter() : base("builtin.utf8-string", CppTypeAdapterKind.Utf8String) { }
        public override bool CanAdapt(CppType type, CppTypeAdapterContext context) => context.Configuration.IsUtf8StringTypeCore(type);
        public override CppTypeAdapterPlan CreatePlan(CppType type, CppTypeAdapterContext context) =>
            new(Name, Kind, "const char*", MarshallingStrategy.String, BindingOwnership.Borrowed);
    }

    private sealed class SharedOwnerAdapter : BuiltInAdapter
    {
        public SharedOwnerAdapter() : base("builtin.shared-owner", CppTypeAdapterKind.SharedOwner) { }
        public override bool CanAdapt(CppType type, CppTypeAdapterContext context) => context.Configuration.IsSharedPtrTypeCore(type);
        public override CppTypeAdapterPlan CreatePlan(CppType type, CppTypeAdapterContext context) =>
            new(Name, Kind, context.Configuration.GetSharedPtrHolderName(type) + "*", MarshallingStrategy.Pointer, BindingOwnership.Shared);
    }

    private sealed class UniqueOwnerAdapter : BuiltInAdapter
    {
        public UniqueOwnerAdapter() : base("builtin.unique-owner", CppTypeAdapterKind.UniqueOwner) { }
        public override bool CanAdapt(CppType type, CppTypeAdapterContext context) => context.Configuration.IsUniquePtrTypeCore(type);
        public override CppTypeAdapterPlan CreatePlan(CppType type, CppTypeAdapterContext context) =>
            ElementPointer(type, context, MarshallingStrategy.Pointer, BindingOwnership.Transferred, true);
    }

    private sealed class SpanAdapter : BuiltInAdapter
    {
        public SpanAdapter() : base("builtin.span", CppTypeAdapterKind.Span) { }
        public override bool CanAdapt(CppType type, CppTypeAdapterContext context) => context.Configuration.IsSpanTypeCore(type);
        public override CppTypeAdapterPlan CreatePlan(CppType type, CppTypeAdapterContext context) =>
            ElementPointer(type, context, MarshallingStrategy.Span, BindingOwnership.Borrowed);
    }

    private sealed class VectorAdapter : BuiltInAdapter
    {
        public VectorAdapter() : base("builtin.vector", CppTypeAdapterKind.Vector) { }
        public override bool CanAdapt(CppType type, CppTypeAdapterContext context) => context.Configuration.IsVectorTypeCore(type);
        public override CppTypeAdapterPlan CreatePlan(CppType type, CppTypeAdapterContext context) =>
            ElementPointer(type, context, MarshallingStrategy.Span, BindingOwnership.Borrowed);
    }

    private sealed class OptionalAdapter : BuiltInAdapter
    {
        public OptionalAdapter() : base("builtin.optional", CppTypeAdapterKind.Optional) { }
        public override bool CanAdapt(CppType type, CppTypeAdapterContext context) => context.Configuration.IsOptionalTypeCore(type);
        public override CppTypeAdapterPlan CreatePlan(CppType type, CppTypeAdapterContext context)
        {
            if (!context.Configuration.TryGetTemplateElementType(type, out CppType? elementType))
                throw new NotSupportedException($"Unable to resolve optional element type '{type}'.");
            bool cleanup = !context.Configuration.IsBlittableBridgeType(elementType!);
            string cType = context.Configuration.GetCType(elementType!) + (cleanup ? "*" : string.Empty);
            return new(Name, Kind, cType, MarshallingStrategy.Optional,
                cleanup ? BindingOwnership.Owned : BindingOwnership.Borrowed, cleanup);
        }
    }

    private sealed class ArrayAdapter : BuiltInAdapter
    {
        public ArrayAdapter() : base("builtin.array", CppTypeAdapterKind.Array) { }
        public override bool CanAdapt(CppType type, CppTypeAdapterContext context) => context.Configuration.IsArrayTypeCore(type);
        public override CppTypeAdapterPlan CreatePlan(CppType type, CppTypeAdapterContext context) =>
            ElementPointer(type, context, MarshallingStrategy.Span, BindingOwnership.Borrowed);
    }

    private abstract class OpaqueValueAdapter(string name, CppTypeAdapterKind kind) : BuiltInAdapter(name, kind)
    {
        protected abstract bool Matches(CppType type, Cpp2CGeneratorConfig configuration);
        public override bool CanAdapt(CppType type, CppTypeAdapterContext context) => Matches(type, context.Configuration);
        public override CppTypeAdapterPlan CreatePlan(CppType type, CppTypeAdapterContext context)
        {
            context.Configuration.ValidateOpaqueAdapterArguments(type, Kind);
            string holder = context.Configuration.GetOpaqueValueHolderName(type);
            bool owns = context.Use == CppTypeAdapterUse.Return;
            return new(Name, Kind, holder + "*", MarshallingStrategy.Handle,
                owns ? BindingOwnership.Owned : BindingOwnership.Borrowed,
                owns, owns ? holder + "Destroy" : null);
        }
    }

    private sealed class MapAdapter : OpaqueValueAdapter
    {
        public MapAdapter() : base("builtin.map", CppTypeAdapterKind.Map) { }
        protected override bool Matches(CppType type, Cpp2CGeneratorConfig configuration) => configuration.IsMapTypeCore(type);
    }

    private sealed class SetAdapter : OpaqueValueAdapter
    {
        public SetAdapter() : base("builtin.set", CppTypeAdapterKind.Set) { }
        protected override bool Matches(CppType type, Cpp2CGeneratorConfig configuration) => configuration.IsSetTypeCore(type);
    }

    private sealed class VariantAdapter : OpaqueValueAdapter
    {
        public VariantAdapter() : base("builtin.variant", CppTypeAdapterKind.Variant) { }
        protected override bool Matches(CppType type, Cpp2CGeneratorConfig configuration) => configuration.IsVariantTypeCore(type);
    }

    private sealed class ExpectedAdapter : OpaqueValueAdapter
    {
        public ExpectedAdapter() : base("builtin.expected", CppTypeAdapterKind.Expected) { }
        protected override bool Matches(CppType type, Cpp2CGeneratorConfig configuration) => configuration.IsExpectedTypeCore(type);
    }

    private sealed class PathAdapter : BuiltInAdapter
    {
        public PathAdapter() : base("builtin.path", CppTypeAdapterKind.Path) { }
        public override bool CanAdapt(CppType type, CppTypeAdapterContext context) => context.Configuration.IsPathTypeCore(type);
        public override CppTypeAdapterPlan CreatePlan(CppType type, CppTypeAdapterContext context) =>
            new(Name, Kind, "const char*", MarshallingStrategy.String, BindingOwnership.Borrowed);
    }

    private sealed class ChronoDurationAdapter : BuiltInAdapter
    {
        public ChronoDurationAdapter() : base("builtin.chrono-duration", CppTypeAdapterKind.ChronoDuration) { }
        public override bool CanAdapt(CppType type, CppTypeAdapterContext context) => context.Configuration.IsChronoDurationTypeCore(type);
        public override CppTypeAdapterPlan CreatePlan(CppType type, CppTypeAdapterContext context) =>
            new(Name, Kind, "int64_t", MarshallingStrategy.Blittable, BindingOwnership.Borrowed);
    }

    private sealed class ChronoTimePointAdapter : BuiltInAdapter
    {
        public ChronoTimePointAdapter() : base("builtin.chrono-time-point", CppTypeAdapterKind.ChronoTimePoint) { }
        public override bool CanAdapt(CppType type, CppTypeAdapterContext context) => context.Configuration.IsChronoTimePointTypeCore(type);
        public override CppTypeAdapterPlan CreatePlan(CppType type, CppTypeAdapterContext context) =>
            new(Name, Kind, "int64_t", MarshallingStrategy.Blittable, BindingOwnership.Borrowed);
    }
}
