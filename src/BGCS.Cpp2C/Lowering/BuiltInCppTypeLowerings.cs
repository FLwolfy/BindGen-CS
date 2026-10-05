using System;
using System.Collections.Generic;
using BGCS.Core.Extensibility;
using BGCS.Cpp2C.Configuration;
using BGCS.CppAst.Model.Types;
using BGCS.Intermediate;

namespace BGCS.Cpp2C.Lowering;

internal static class BuiltInCppTypeLowerings
{
    internal static IReadOnlyList<ICppTypeLowering> all { get; } = [new Utf8StringLowering(), new SharedOwnerLowering(), new UniqueOwnerLowering(), new SpanLowering(), new VectorLowering(), new OptionalLowering(), new ArrayLowering(), new MapLowering(), new SetLowering(), new VariantLowering(), new ExpectedLowering(), new PathLowering(), new ChronoDurationLowering(), new ChronoTimePointLowering()];

    private abstract class BuiltInLowering(
        string name,
        CppTypeLoweringKind kind
    ) : ICppTypeLowering, ICacheFingerprintProvider
    {
        public string name { get; } = name;
        public int priority => 0;
        protected CppTypeLoweringKind kind { get; } = kind;

        public abstract bool CanLower(
            CppType type,
            CppTypeLoweringContext context
        );
        public abstract CppTypeLoweringPlan CreatePlan(
            CppType type,
            CppTypeLoweringContext context
        );
        public string GetCacheFingerprint() => $"{GetType().Module.ModuleVersionId:N}:{this.name}";
        protected CppTypeLoweringPlan ElementPointer(
            CppType type,
            CppTypeLoweringContext context,
            MarshallingStrategy strategy,
            BindingOwnership ownership,
            bool cleanup = false
        ) {
            if (!context.configuration.TryGetTemplateElementType(type, out CppType? elementType))
                throw new NotSupportedException($"Unable to resolve lowering element type '{type}'.");
            return new(this.name, this.kind, context.configuration.GetCType(elementType!) + "*", strategy, ownership)
            {
                abiShape = CppAbiShape.PointerAndLength,
                requiresCleanup = cleanup,
                cleanupFunction = cleanup ? context.configuration.GetCType(elementType!) + "Destroy" : null,
                allocatorKind = cleanup ? BindingAllocatorKind.NativeFunction : BindingAllocatorKind.Unspecified
            };
        }
    }

    private sealed class Utf8StringLowering : BuiltInLowering
    {
        public Utf8StringLowering() : base("builtin.utf8-string", CppTypeLoweringKind.Utf8String)
        {
        }

        public override bool CanLower(
            CppType type,
            CppTypeLoweringContext context
        ) => context.configuration.IsUtf8StringTypeCore(type);
        public override CppTypeLoweringPlan CreatePlan(
            CppType type,
            CppTypeLoweringContext context
        ) => new(this.name, this.kind, "const char*", MarshallingStrategy.String, BindingOwnership.Borrowed);
    }

    private sealed class SharedOwnerLowering : BuiltInLowering
    {
        public SharedOwnerLowering() : base("builtin.shared-owner", CppTypeLoweringKind.SharedOwner)
        {
        }

        public override bool CanLower(
            CppType type,
            CppTypeLoweringContext context
        ) => context.configuration.IsSharedPtrTypeCore(type);
        public override CppTypeLoweringPlan CreatePlan(
            CppType type,
            CppTypeLoweringContext context
        ) => new(this.name, this.kind, context.configuration.GetSharedPtrHolderName(type) + "*", MarshallingStrategy.Pointer, BindingOwnership.Shared);
    }

    private sealed class UniqueOwnerLowering : BuiltInLowering
    {
        public UniqueOwnerLowering() : base("builtin.unique-owner", CppTypeLoweringKind.UniqueOwner)
        {
        }

        public override bool CanLower(
            CppType type,
            CppTypeLoweringContext context
        ) => context.configuration.IsUniquePtrTypeCore(type);
        public override CppTypeLoweringPlan CreatePlan(
            CppType type,
            CppTypeLoweringContext context
        ) => ElementPointer(type, context, MarshallingStrategy.Pointer, BindingOwnership.Transferred, true);
    }

    private sealed class SpanLowering : BuiltInLowering
    {
        public SpanLowering() : base("builtin.span", CppTypeLoweringKind.Span)
        {
        }

        public override bool CanLower(
            CppType type,
            CppTypeLoweringContext context
        ) => context.configuration.IsSpanTypeCore(type);
        public override CppTypeLoweringPlan CreatePlan(
            CppType type,
            CppTypeLoweringContext context
        ) => ElementPointer(type, context, MarshallingStrategy.Span, BindingOwnership.Borrowed);
    }

    private sealed class VectorLowering : BuiltInLowering
    {
        public VectorLowering() : base("builtin.vector", CppTypeLoweringKind.Vector)
        {
        }

        public override bool CanLower(
            CppType type,
            CppTypeLoweringContext context
        ) => context.configuration.IsVectorTypeCore(type);
        public override CppTypeLoweringPlan CreatePlan(
            CppType type,
            CppTypeLoweringContext context
        ) => ElementPointer(type, context, MarshallingStrategy.Span, BindingOwnership.Borrowed);
    }

    private sealed class OptionalLowering : BuiltInLowering
    {
        public OptionalLowering() : base("builtin.optional", CppTypeLoweringKind.Optional)
        {
        }

        public override bool CanLower(
            CppType type,
            CppTypeLoweringContext context
        ) => context.configuration.IsOptionalTypeCore(type);
        public override CppTypeLoweringPlan CreatePlan(
            CppType type,
            CppTypeLoweringContext context
        ) {
            if (!context.configuration.TryGetTemplateElementType(type, out CppType? elementType))
                throw new NotSupportedException($"Unable to resolve optional element type '{type}'.");
            bool cleanup = !context.configuration.IsBlittableBridgeType(elementType!);
            string cType = context.configuration.GetCType(elementType!) + (cleanup ? "*" : string.Empty);
            return new(this.name, this.kind, cType, MarshallingStrategy.Optional, cleanup ? BindingOwnership.Owned : BindingOwnership.Borrowed)
            {
                abiShape = CppAbiShape.OptionalValue,
                requiresCleanup = cleanup,
                cleanupFunction = cleanup ? context.configuration.GetCType(elementType!) + "Destroy" : null,
                allocatorKind = cleanup ? BindingAllocatorKind.NativeFunction : BindingAllocatorKind.Unspecified
            };
        }
    }

    private sealed class ArrayLowering : BuiltInLowering
    {
        public ArrayLowering() : base("builtin.array", CppTypeLoweringKind.Array)
        {
        }

        public override bool CanLower(
            CppType type,
            CppTypeLoweringContext context
        ) => context.configuration.IsArrayTypeCore(type);
        public override CppTypeLoweringPlan CreatePlan(
            CppType type,
            CppTypeLoweringContext context
        ) => ElementPointer(type, context, MarshallingStrategy.Span, BindingOwnership.Borrowed);
    }

    private abstract class OpaqueValueLowering(
        string name,
        CppTypeLoweringKind kind
    ) : BuiltInLowering(name, kind)
    {
        protected abstract bool Matches(
            CppType type,
            Cpp2CGeneratorConfig configuration
        );
        public override bool CanLower(
            CppType type,
            CppTypeLoweringContext context
        ) => Matches(type, context.configuration);
        public override CppTypeLoweringPlan CreatePlan(
            CppType type,
            CppTypeLoweringContext context
        ) {
            context.configuration.ValidateOpaqueLoweringArguments(type, this.kind);
            string holder = context.configuration.GetOpaqueValueHolderName(type);
            bool owns = context.use == CppTypeLoweringUse.Return;
            return new(this.name, this.kind, holder + "*", MarshallingStrategy.Handle, owns ? BindingOwnership.Owned : BindingOwnership.Borrowed)
            {
                abiShape = CppAbiShape.OpaqueHandle,
                requiresCleanup = owns,
                cleanupFunction = owns ? holder + "Destroy" : null,
                allocatorKind = owns ? BindingAllocatorKind.NativeFunction : BindingAllocatorKind.Unspecified
            };
        }
    }

    private sealed class MapLowering : OpaqueValueLowering
    {
        public MapLowering() : base("builtin.map", CppTypeLoweringKind.Map)
        {
        }

        protected override bool Matches(
            CppType type,
            Cpp2CGeneratorConfig configuration
        ) => configuration.IsMapTypeCore(type);
    }

    private sealed class SetLowering : OpaqueValueLowering
    {
        public SetLowering() : base("builtin.set", CppTypeLoweringKind.Set)
        {
        }

        protected override bool Matches(
            CppType type,
            Cpp2CGeneratorConfig configuration
        ) => configuration.IsSetTypeCore(type);
    }

    private sealed class VariantLowering : OpaqueValueLowering
    {
        public VariantLowering() : base("builtin.variant", CppTypeLoweringKind.Variant)
        {
        }

        protected override bool Matches(
            CppType type,
            Cpp2CGeneratorConfig configuration
        ) => configuration.IsVariantTypeCore(type);
    }

    private sealed class ExpectedLowering : OpaqueValueLowering
    {
        public ExpectedLowering() : base("builtin.expected", CppTypeLoweringKind.Expected)
        {
        }

        protected override bool Matches(
            CppType type,
            Cpp2CGeneratorConfig configuration
        ) => configuration.IsExpectedTypeCore(type);
    }

    private sealed class PathLowering : BuiltInLowering
    {
        public PathLowering() : base("builtin.path", CppTypeLoweringKind.Path)
        {
        }

        public override bool CanLower(
            CppType type,
            CppTypeLoweringContext context
        ) => context.configuration.IsPathTypeCore(type);
        public override CppTypeLoweringPlan CreatePlan(
            CppType type,
            CppTypeLoweringContext context
        ) => new(this.name, this.kind, "const char*", MarshallingStrategy.String, BindingOwnership.Borrowed);
    }

    private sealed class ChronoDurationLowering : BuiltInLowering
    {
        public ChronoDurationLowering() : base("builtin.chrono-duration", CppTypeLoweringKind.ChronoDuration)
        {
        }

        public override bool CanLower(
            CppType type,
            CppTypeLoweringContext context
        ) => context.configuration.IsChronoDurationTypeCore(type);
        public override CppTypeLoweringPlan CreatePlan(
            CppType type,
            CppTypeLoweringContext context
        ) => new(this.name, this.kind, "int64_t", MarshallingStrategy.Blittable, BindingOwnership.Borrowed);
    }

    private sealed class ChronoTimePointLowering : BuiltInLowering
    {
        public ChronoTimePointLowering() : base("builtin.chrono-time-point", CppTypeLoweringKind.ChronoTimePoint)
        {
        }

        public override bool CanLower(
            CppType type,
            CppTypeLoweringContext context
        ) => context.configuration.IsChronoTimePointTypeCore(type);
        public override CppTypeLoweringPlan CreatePlan(
            CppType type,
            CppTypeLoweringContext context
        ) => new(this.name, this.kind, "int64_t", MarshallingStrategy.Blittable, BindingOwnership.Borrowed);
    }
}
