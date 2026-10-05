using BGCS.Core.Extensibility;
using BGCS.Cpp2C.Lowering;
using BGCS.CppAst.Model.Declarations;

namespace BGCS.Examples.LoweringPlugin;

/// <summary>
/// Demonstrates callable renaming through an independently loaded generation plugin.
/// </summary>
public sealed class EngineLoweringPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc />
    public string id => "bgcs.examples.engine-lowering";
    /// <inheritdoc />
    public string version => "1.0.0";
    /// <inheritdoc />
    public int contractVersion => BindingPluginContract.C_CURRENT_VERSION;

    /// <inheritdoc />
    public void Configure(IBindingPluginHost host)
    {
        host.Register<ICppCallableLowering>(
            "engine.rename-tick",
            new RenameTickLowering(),
            priority: 100);
    }

    /// <inheritdoc />
    public string GetCacheFingerprint() => "rename-tick";

    private sealed class RenameTickLowering : ICppCallableLowering, ICacheFingerprintProvider
    {
        /// <inheritdoc />
        public string name => "engine.rename-tick";
        /// <inheritdoc />
        public int priority => 100;

        /// <inheritdoc />
        public bool CanLower(
            CppFunction function,
            CppCallableLoweringContext context
        ) =>
            function.name == "Tick" && context.declaringType?.fullName == "Engine::World";

        /// <inheritdoc />
        public CppCallableLoweringPlan CreatePlan(
            CppFunction function,
            CppCallableLoweringContext context
        ) => new(name, "engine_world_tick");

        /// <inheritdoc />
        public string GetCacheFingerprint() => "engine-world-tick";
    }
}
