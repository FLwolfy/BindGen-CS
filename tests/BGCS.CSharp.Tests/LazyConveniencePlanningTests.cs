using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Managed;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class LazyConveniencePlanningTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UninitializedConvenienceArraysStillRejectBeforeCandidateWrites(bool defaults)
    {
        ManagedApiModel healthy = NeutralBackendTests.Model();
        ManagedMember member = healthy.methods[0];
        ManagedApiModel malformed = healthy with
        {
            methods = healthy.methods.SetItem(0, defaults
                ? member with { defaults = default }
                : member with { inputVariations = default })
        };
        await AssertRejectedThenHealthy(malformed, healthy,
            defaults ? "Managed default arguments must be initialized." : "Managed input variations must be initialized.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public async Task InvalidDefaultLimitsStillRejectWhenNoConvenienceWasRequested(int limit)
    {
        ManagedApiModel healthy = NeutralBackendTests.Model();
        Assert.All(healthy.methods, member => { Assert.Empty(member.defaults); Assert.Empty(member.inputVariations); });
        await AssertRejectedThenHealthy(healthy with { defaultOverloadLimit = limit }, healthy,
            "The default overload limit must be between 1 and 4096.");
    }

    [Fact]
    public async Task NoConvenienceRequestsPreserveAndOrderIndependentRecipeProjections()
    {
        ManagedRecipeHelper first = new("first", "int", new("long", "checked((int){value})", "(long){value}"));
        ManagedRecipeHelper second = new("second", "int", new("uint", "checked((int){value})", "checked((uint){value})"));
        ManagedApiModel model = NeutralBackendTests.Model() with { recipeHelpers = [second, first] };
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = PlanningFixture.Prepare(model, new("fixture", sink));
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, request, CancellationToken.None);
        Assert.Empty(plan.defaultOverloads);
        Assert.False(plan.defaultOverloads.IsDefault);
        Assert.Empty(plan.inputVariations);
        Assert.False(plan.inputVariations.IsDefault);
        string[] expected = new[] { first, second }.Select(helper => new ManagedRecipeProjectionValidator()
            .Plan(helper.projection, helper.cAbiType, model.interop.pointerSize).id).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, plan.recipeProjections.Select(projection => projection.id));
        Assert.Equal(new[] { "first", "second" }, plan.recipeAliases.Select(alias => alias.name).Order(StringComparer.Ordinal));
        await new CSharpBackend().EmitAsync(model, request, CancellationToken.None);
        string helpers = sink.sources["Extensions/ConfiguredLoweringProjections.g.cs"];
        foreach (string id in expected)
        {
            Assert.Contains("__ToNative_" + id, helpers, StringComparison.Ordinal);
            Assert.Contains("__FromNative_" + id, helpers, StringComparison.Ordinal);
        }
        foreach (string name in new[] { "first", "second" })
        {
            Assert.Contains("ToNative_" + name, helpers, StringComparison.Ordinal);
            Assert.Contains("FromNative_" + name, helpers, StringComparison.Ordinal);
        }
    }

    private static async Task AssertRejectedThenHealthy(ManagedApiModel malformed, ManagedApiModel healthy, string expected)
    {
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = PlanningFixture.Prepare(malformed, new("fixture", sink));
        ArgumentException planning = Assert.Throws<ArgumentException>(() => new CSharpEmissionPlanner().Plan(malformed, request, CancellationToken.None));
        Assert.Contains(expected, planning.Message, StringComparison.Ordinal);
        ArgumentException emission = await Assert.ThrowsAsync<ArgumentException>(async () => await new CSharpBackend()
            .EmitAsync(malformed, request, CancellationToken.None));
        Assert.Contains(expected, emission.Message, StringComparison.Ordinal);
        Assert.Empty(sink.sources);
        await new CSharpBackend().EmitAsync(healthy, PlanningFixture.Prepare(healthy, new("fixture", sink)), CancellationToken.None);
        Assert.NotEmpty(sink.sources);
    }
}
