using BGCS.Core.Extensibility;
using BGCS.Cpp2C.Lowering;
using BGCS.CppAst.Model.Declarations;

namespace BGCS.Examples.LoweringPlugin;

public sealed class EngineLoweringPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    public string Id => "bgcs.examples.engine-lowering";
    public string Version => "1.0.0";
    public int ContractVersion => BindingPluginContract.CurrentVersion;

    public void Configure(IBindingPluginHost host)
    {
        host.Register<ICppCallableLowering>(
            "engine.rename-tick",
            new RenameTickLowering(),
            priority: 100);
    }

    public string GetCacheFingerprint() => "rename-tick";

    private sealed class RenameTickLowering : ICppCallableLowering, ICacheFingerprintProvider
    {
        public string Name => "engine.rename-tick";
        public int Priority => 100;

        public bool CanLower(CppFunction function, CppCallableLoweringContext context) =>
            function.Name == "Tick" && context.DeclaringType?.FullName == "Engine::World";

        public CppCallableLoweringPlan CreatePlan(
            CppFunction function,
            CppCallableLoweringContext context) =>
            new(Name, "engine_world_tick");

        public string GetCacheFingerprint() => "engine-world-tick";
    }
}
