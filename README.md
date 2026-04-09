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
- Optional standalone runtime source generation (`Runtime.cs`)
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

A basic start up with configuration file:

```json
{
  "BaseConfig": {
    "Url": "file://config.base.json"
  }
  "ApiName": "MyApi",
  "Namespace": "My.Generated",
  "LibName": "mylib",
  "ImportType": "DllImport",
  "EntryFiles": ["headers/api.h"],
  "allowedHeaders": ["headers/api.h"]
}
```

```C#
using BGCS;

var cfg = new CsCodeGeneratorConfig("config.json");
var gen = new CsCodeGenerator(cfg);
bool ok = gen.Generate("headers/api.h", "Output");
```

Single-file/runtime behavior summary:

- When `MergeGeneratedFilesToSingleFile = true`, BGCS merges output into `SingleFileOutputName` and automatically removes split generated files.
- Runtime source generation is explicit:
  - `GenerateRuntimeSource = true`: generate standalone `Runtime.cs` in output root.
  - `GenerateRuntimeSource = false`: do not generate runtime source.
- Generated bindings always use `using BGCS.Runtime;` and runtime namespace remains `BGCS.Runtime`.
- If your project already references external `BGCS.Runtime`, define compile symbol `BGCS_RUNTIME_EXTERNAL` to exclude generated runtime source and avoid duplicate type declarations.

### Full Documents

- [Basic API Usage](docs/api.md)
- [Config Settings for BGCS](docs/config.md)
- [Config Settings for Cpp2C](docs/cpp2c.config.md)

## Project Layout

- `src/BGCS` - Main C# binding generator
- `src/BGCS.Cpp2C` - C++ to C bridge generator
- `src/BGCS.Runtime` - Runtime support for generated bindings
- `src/BGCS.CppAst` - C/C++ AST parsing layer
- `tests/*` - Unit/integration/regression test projects
- `demo/*` - Local demo apps for manual generation validation


## License

BindGen-CS is licensed under the MIT License. See [LICENSE](LICENSE).
