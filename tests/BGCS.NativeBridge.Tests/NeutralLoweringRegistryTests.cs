using System;
using System.Collections.Generic;
using System.Threading;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.NativeBridge.Planning;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class NeutralLoweringRegistryTests
{
    [Fact]
    public void PriorityTiers_UseStableIdentityOrderAndDoNotEvaluateLowerTiers()
    {
        LoweringContext context = Context();
        List<string> trace = [];
        var registry = new LoweringRegistry([
            (new("low", ExtensionPort.NativeTypeLowering, 0), new TypeLowering("low", trace, true)),
            (new("z-high", ExtensionPort.NativeTypeLowering, 10), new TypeLowering("z-high", trace, true)),
            (new("a-high", ExtensionPort.NativeTypeLowering, 10), new TypeLowering("a-high", trace, false))]);
        Assert.NotNull(registry.LowerType(new(context.module, context.module.types[0].id), context, CancellationToken.None));
        Assert.Equal(new[] { "match:a-high", "match:z-high", "lower:z-high" }, trace);
    }

    [Fact]
    public void DuplicateIdsAndCompetingExclusiveClaims_AreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => new LoweringRegistry([
            (new("same", ExtensionPort.NativeTypeLowering), new TypeLowering("a", [], true)),
            (new("same", ExtensionPort.NativeTypeLowering, 1), new TypeLowering("b", [], true))]));
        LoweringContext context = Context();
        var registry = new LoweringRegistry([
            (new("first", ExtensionPort.NativeTypeLowering, 5), new TypeLowering("first", [], true)),
            (new("second", ExtensionPort.NativeTypeLowering, 5), new TypeLowering("second", [], true))]);
        Assert.Throws<InvalidOperationException>(() => registry.LowerType(new(context.module, context.module.types[0].id), context, CancellationToken.None));
    }

    [Fact]
    public void ReplacingCallerDescriptorDoesNotAlterFrozenSessionOrder()
    {
        LoweringContext context = Context();
        List<string> trace = [];
        ExtensionDescriptor descriptor = new("changing", ExtensionPort.NativeTypeLowering, 5);
        var registry = new LoweringRegistry([(descriptor, new TypeLowering("first", trace, true)),
            (new("other", ExtensionPort.NativeTypeLowering), new TypeLowering("second", trace, true))]);
        descriptor = descriptor with { priority = -100 };
        Assert.NotNull(registry.LowerType(new(context.module, context.module.types[0].id), context, CancellationToken.None));
        Assert.Equal(new[] { "match:first", "lower:first" }, trace);
        Assert.Equal(-100, descriptor.priority);
    }

    [Fact]
    public void MutableMatchingStateCannotReplaceAuthoritativeRegistrationIdentity()
    {
        LoweringContext context = Context();
        TypeLowering service = new("mutable-state", [], true);
        var registry = new LoweringRegistry([(new("authoritative-id", ExtensionPort.NativeTypeLowering), service)]);
        Assert.NotNull(registry.LowerType(new(context.module, context.module.types[0].id), context, CancellationToken.None));
        Assert.NotNull(registry.LowerType(new(context.module, context.module.types[0].id), context, CancellationToken.None));
        Assert.Equal(2, service.matches);
    }

    private static LoweringContext Context()
    {
        NativeType integer = new(new StableId("int"), NativeTypeCategory.Primitive, "int", 4, 4) { primitiveKind = "Int", isSigned = true };
        NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [integer], [], []) { language = SourceLanguage.Cpp };
        NativeTargetDescriptor target = new(new("windows-x64-msvc"), "windows", "x64", "msvc", module.triple, new());
        return new(module, target, "example", []);
    }
    private sealed class TypeLowering(string identity, List<string> trace, bool match) : INativeTypeLowering
    {
        internal int matches { get; private set; }
        public bool CanLower(NativeTypeView type, LoweringContext context) { matches++; trace.Add("match:" + identity); return match; }
        public NativeTypeLoweringPlan Lower(NativeTypeView type, LoweringContext context, CancellationToken cancellationToken)
        {
            trace.Add("lower:" + identity);
            BridgeType carrier = new(new("custom-int"), BridgeTypeKind.Scalar, "int32_t") { nativeType = type.id };
            return new(carrier.id, [carrier], BridgeConversionKind.ScalarCast, BridgeConversionKind.ScalarCast);
        }
    }
}
