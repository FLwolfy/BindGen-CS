using System;
using System.Collections.Immutable;
using System.Threading;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Cpp2C.Planning;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class NeutralLoweringRegistryTests
{
    [Fact]
    public void PriorityTiers_UseStableIdentityOrderAndDoNotEvaluateLowerTiers()
    {
        LoweringContext context = Context();
        System.Collections.Generic.List<string> trace = [];
        var registry = new LoweringRegistry([new TypeLowering("low", 0, trace, true), new TypeLowering("z-high", 10, trace, true), new TypeLowering("a-high", 10, trace, false)]);
        var result = registry.LowerType(new(context.module, context.module.types[0].id), context, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(new[] { "match:a-high", "match:z-high", "lower:z-high" }, trace);
    }

    [Fact]
    public void DuplicateIdsAndCompetingExclusiveClaims_AreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => new LoweringRegistry([new TypeLowering("same", 0, [], true), new TypeLowering("same", 1, [], true)]));
        LoweringContext context = Context();
        var registry = new LoweringRegistry([new TypeLowering("first", 5, [], true), new TypeLowering("second", 5, [], true)]);
        Assert.Throws<InvalidOperationException>(() => registry.LowerType(new(context.module, context.module.types[0].id), context, CancellationToken.None));
    }

    [Fact]
    public void ChangedServicePriority_DoesNotAlterTheFrozenSessionOrder()
    {
        LoweringContext context = Context();
        TypeLowering service = new("changing", 5, [], true);
        var registry = new LoweringRegistry([service]);
        service.priority = 99;
        Assert.Contains("frozen", Assert.Throws<InvalidOperationException>(() => registry.LowerType(new(context.module, context.module.types[0].id), context, CancellationToken.None)).Message);
    }

    [Fact]
    public void MatchingCannotMutateTheFrozenServiceIdentity()
    {
        LoweringContext context = Context();
        var registry = new LoweringRegistry([new MutatingLowering()]);
        Assert.Contains("during matching", Assert.Throws<InvalidOperationException>(() => registry.LowerType(new(context.module, context.module.types[0].id), context, CancellationToken.None)).Message);
    }

    private sealed class MutatingLowering : BGCS.Contracts.Ports.ICppTypeLowering
    {
        public string id => "mutating";
        public int priority { get; private set; }
        public bool CanLower(NativeTypeView type, LoweringContext context) { priority++; return true; }
        public CppTypeLoweringPlan Lower(NativeTypeView type, LoweringContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("A mutated match must not reach planning.");
    }

    private static LoweringContext Context()
    {
        NativeType integer = new(new StableId("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [integer], [], []) { language = SourceLanguage.Cpp };
        NativeTargetDescriptor target = new(new("windows-x64-msvc"), "windows", "x64", "msvc", module.triple, new());
        return new(module, target, "example", []);
    }

    private sealed class TypeLowering(string identity, int order, System.Collections.Generic.List<string> trace, bool match) : BGCS.Contracts.Ports.ICppTypeLowering
    {
        public string id => identity;
        public int priority { get; set; } = order;
        public bool CanLower(NativeTypeView type, LoweringContext context) { trace.Add("match:" + identity); return match; }
        public CppTypeLoweringPlan Lower(NativeTypeView type, LoweringContext context, CancellationToken cancellationToken)
        {
            trace.Add("lower:" + identity);
            BridgeType carrier = new(new("custom-int"), BridgeTypeKind.Scalar, "int32_t") { nativeType = type.id };
            return new(carrier.id, [carrier], BridgeConversionKind.ScalarCast, BridgeConversionKind.ScalarCast);
        }
    }
}
