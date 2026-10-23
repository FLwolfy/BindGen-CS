using System;
using System.Linq;
using BGCS.Core.Managed;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ProjectionPlanningReuseTests
{
    [Fact]
    public void EqualConversionsShareOneCheckedValueWithoutSharingAcrossPlanningCalls()
    {
        ManagedRecipeProjection conversion = new("long", "checked((int){value})", "(long){value} + 100");
        ManagedApiModel source = NeutralBackendTests.Model();
        ManagedApiModel model = source with
        {
            methods = source.methods.SetItem(0, source.methods[0] with
            {
                valueProjections = [new(new("left"), conversion), new(new("right"), conversion with { })],
                returnProjection = conversion with { }
            }),
            recipeHelpers = [new("shared", "int", conversion with { })]
        };

        CSharpEmissionPlan first = Plan(model);
        CSharpMethodPlan method = first.methods[0];
        CSharpRecipeProjectionPlan checkedValue = method.valueProjections[0];
        Assert.Same(checkedValue, method.valueProjections[1]);
        Assert.Same(checkedValue, method.returnProjection);
        Assert.Same(checkedValue, Assert.Single(first.recipeProjections));
        Assert.Equal("long", checkedValue.managedType);
        Assert.Equal("int", checkedValue.carrierType);
        Assert.Equal("checked((int)value)", checkedValue.toNativeExpression);
        Assert.Equal("(long)value + 100", checkedValue.fromNativeExpression);
        Assert.Equal("int", method.returnType);
        Assert.Equal(new[] { "int", "int" }, method.parameterTypes.ToArray());
        Assert.Equal(new[] { 0, 1 }, method.valueProjections.Keys.Order().ToArray());
        Assert.Equal(new[] { "left", "right" }, method.parameterNames.ToArray());

        CSharpRecipeProjectionPlan independent = Plan(model).methods[0].valueProjections[0];
        Assert.Equal(checkedValue, independent);
        Assert.NotSame(checkedValue, independent);
        Assert.Same(source.interop, first.api.interop);
    }

    [Fact]
    public void CarrierTypeExpressionsAndNamespaceRemainIndependentInputs()
    {
        ManagedRecipeProjection conversion = new("Int64", "checked((int){value})", "{value}", "System");
        ManagedApiModel model = NeutralBackendTests.Model() with
        {
            recipeHelpers =
            [
                new("original", "int", conversion),
                new("carrier", "long", conversion),
                new("type", "int", conversion with { managedType = "Int32" }),
                new("input", "int", conversion with { managedToNativeExpression = "checked((int)({value} + 1))" }),
                new("output", "int", conversion with { nativeToManagedExpression = "(long){value} + 10" }),
                new("namespace", "int", conversion with { requiredNamespace = null })
            ]
        };
        CSharpEmissionPlan plan = Plan(model);
        Assert.Equal(6, plan.recipeProjections.Length);
        CSharpRecipeProjectionPlan original = Projection("original");
        Assert.Equal("int", original.carrierType);
        Assert.Equal("long", Projection("carrier").carrierType);
        Assert.Equal("global::System.Int64", original.managedType);
        Assert.Equal("global::System.Int32", Projection("type").managedType);
        Assert.Equal("Int64", Projection("namespace").managedType);
        Assert.Equal("checked((int)(value + 1))", Projection("input").toNativeExpression);
        Assert.Equal("(long)value + 10", Projection("output").fromNativeExpression);
        Assert.Equal(6, plan.recipeProjections.Select(projection => projection.id).Distinct(StringComparer.Ordinal).Count());

        CSharpRecipeProjectionPlan Projection(string name)
        {
            string identity = Assert.Single(plan.recipeAliases, alias => alias.name == name).projectionId;
            return Assert.Single(plan.recipeProjections, projection => projection.id == identity);
        }
    }

    [Theory]
    [InlineData("placeholder")]
    [InlineData("expression")]
    [InlineData("type")]
    [InlineData("namespace")]
    public void APreviouslyCheckedConversionDoesNotHideAnInvalidLaterConversion(string invalid)
    {
        ManagedRecipeProjection conversion = new("long", "checked((int){value})", "(long){value}");
        ManagedRecipeProjection malformed = invalid switch
        {
            "placeholder" => conversion with { managedToNativeExpression = "{missing}" },
            "expression" => conversion with { nativeToManagedExpression = "{value} +" },
            "type" => conversion with { managedType = "long<" },
            _ => conversion with { requiredNamespace = "Invalid-Namespace" }
        };
        ManagedApiModel source = NeutralBackendTests.Model();
        ManagedApiModel model = source with
        {
            recipeHelpers = [new("checked-first", "int", conversion)],
            methods = source.methods.SetItem(0, source.methods[0] with
            {
                valueProjections = [new(new("left"), conversion), new(new("right"), malformed)]
            })
        };
        if (invalid == "namespace")
            Assert.Throws<ArgumentException>(() => Plan(model));
        else
            Assert.Throws<InvalidOperationException>(() => Plan(model));
        Assert.Empty(source.methods[0].valueProjections);
    }

    [Fact]
    public void EqualConversionReuseDoesNotPermitConflictingBindingsForOneNativeParameter()
    {
        ManagedRecipeProjection conversion = new("long", "checked((int){value})", "(long){value}");
        ManagedApiModel source = NeutralBackendTests.Model();
        ManagedApiModel model = source with
        {
            recipeHelpers = [new("checked-first", "int", conversion)],
            methods = source.methods.SetItem(0, source.methods[0] with
            {
                valueProjections =
                [
                    new(new("left"), conversion),
                    new(new("right"), conversion with { }),
                    new(new("left"), conversion with { managedToNativeExpression = "checked((int)({value} + 1))" })
                ]
            })
        };
        Assert.Throws<ArgumentException>(() => Plan(model));
    }

    private static CSharpEmissionPlan Plan(ManagedApiModel model)
        => new CSharpEmissionPlanner().Plan(model,
            PlanningFixture.Prepare(model, new EmissionRequest("projection_fixture", new NeutralBackendTests.MemorySink())
            { additionalUsings = ["System"] }));
}
