# BindGen-CS

BindGen-CS is a code generation toolkit for native interop, focused on generating C# bindings from C/C++ headers and generating C bridges from C++ APIs. With proper configurations, this code-generation tool allows you to output a **SINGLE-FILE** bindings.

This project is a modified derivative of a fork of [HexaGen](https://github.com/HexaEngine/HexaGen).
It has been adapted and extended with a focus on a streamlined, single-file-oriented workflow. 
Contributions are welcome ⭐. Feel free to open issues or submit pull requests if you encounter problems or have suggestions.


## Features

- C# binding generation for C-style native APIs
- C++ to C bridge generation (`BGCS.Cpp2C`)
- Config-driven generation with JSON config composition (`BaseConfig`)
- Function-table and direct import modes for generated bindings
- Optional single-file output with embedded runtime source (no external runtime project reference needed)
- Single-file merge auto-removes split generated files
- Configurable naming, filtering, and mapping pipelines
- Runtime support libraries for generated interop code

## Requirements

- .NET SDK 9.0
- Clang/LibClang available in the environment (for parsing C/C++ headers)
- A C/C++ toolchain for validating generated native bridge outputs when needed

## Documentation

### Quick Start Cases

BGCS (C/C++ header -> C# bindings):

```csharp
using BGCS;

var config = CsCodeGeneratorConfig.Load("config.json");
var generator = new CsCodeGenerator(config);
bool ok = generator.Generate("headers/api.h", "Output");
```

Cpp2C (C++ header -> C bridge):

```csharp
using BGCS.Cpp2C;

var config = Cpp2CGeneratorConfig.Load("config.json");
var generator = new Cpp2CCodeGenerator(config);
generator.Generate("include/demo.hpp", "Output");
```

Config composition (`BaseConfig`) is supported via:

```json
{
  "BaseConfig": {
    "Url": "file://config.base.json"
  }
}
```

Single-file/runtime behavior summary:

- When `MergeGeneratedFilesToSingleFile = true`, BGCS merges output into `SingleFileOutputName` and automatically removes split generated files.
- When runtime is required:
  - `IncludeRuntimeSourceInSingleFile = true`: runtime source is embedded into the merged binding file.
  - `IncludeRuntimeSourceInSingleFile = false`: runtime source is emitted as standalone `Runtime.cs`.
- Generated bindings always use `using BGCS.Runtime;` and runtime namespace remains `BGCS.Runtime`.

### Full Documents

- [document.en.md](docs/document.en.md) (English)
- [document.cn.md](docs/document.cn.md) (中文)

## Project Layout

- `src/BGCS` - Main C# binding generator
- `src/BGCS.Cpp2C` - C++ to C bridge generator
- `src/BGCS.Runtime` - Runtime support for generated bindings
- `src/BGCS.CppAst` - C/C++ AST parsing layer
- `tests/*` - Unit/integration/regression test projects
- `demo/*` - Local demo apps for manual generation validation


## License

BindGen-CS is licensed under the MIT License. See [LICENSE](LICENSE).
