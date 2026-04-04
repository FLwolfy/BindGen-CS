# BindGen-CS

BindGen-CS is a code generation toolkit for native interop, focused on generating C# bindings from C/C++ headers and generating C bridges from C++ APIs.

This project is a **modified version of a forked HexaGen codebase**, adapted and extended for the BindGen-CS workflow and architecture.


## Features

- C# binding generation for C-style native APIs
- C++ to C bridge generation (`BGCS.Cpp2C`)
- Config-driven generation with JSON config composition (`BaseConfig`)
- Function-table and direct import modes for generated bindings
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
