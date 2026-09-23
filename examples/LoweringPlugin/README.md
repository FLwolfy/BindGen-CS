# Typed lowering plugin example

Build the independent plugin assembly:

```bash
dotnet build BGCS.Example.LoweringPlugin.csproj --configuration Release
```

Load it from a bridge configuration whose path is relative to this directory:

```json
{
  "PluginAssemblies": [
    "bin/Release/net9.0/BGCS.Example.LoweringPlugin.dll"
  ]
}
```

The plugin matches `Engine::World::Tick` from the parsed AST and assigns the stable export name `engine_world_tick`. It is a separate project and does not modify BindGen-CS source. See the [C++ extension cookbook](../../docs/cpp-extension-cookbook.md) for type, callable, and artifact extension patterns.
