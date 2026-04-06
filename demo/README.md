# BGCS Demo Workspace

This folder contains two standalone demo apps extracted from the repository tests:

- `BGCS.Demo`: C/C++ -> C# bindings demo via `BGCS`
- `BGCS.Cpp2C.Demo`: C++ -> C bridge generation demo via `BGCS.Cpp2C`

## Prerequisites

- .NET SDK (`net9.0`)
- Clang/LibClang available to the parser environment
- This repo present at `~/Dev/BindGen-CS` (project references are wired to this path)

## 1) BGCS.Demo

Location: `~/demo/BGCS.Demo`

Run with default config:

```bash
cd ~/demo/BGCS.Demo
dotnet run
```

Run with explicit config/output:

```bash
cd ~/demo/BGCS.Demo
dotnet run -- config.runtime-notgenerated.json Output
```

Run with runtime source generation enabled:

```bash
cd ~/demo/BGCS.Demo
dotnet run -- config.runtime-generated.json Output
```

Run with all configuration fields explicitly set (showcase profile):

```bash
cd ~/demo/BGCS.Demo
dotnet run -- config.all-set.json Output
```

Config notes:

- `config.runtime-generated.json`: generate `Bindings.cs` + standalone `Runtime.cs` (`GenerateRuntimeSource=true`)
- `config.runtime-notgenerated.json`: generate only `Bindings.cs` (`GenerateRuntimeSource=false`)
- `config.all-set.json`: comprehensive showcase config with explicit values for nearly all BGCS options
- `RuntimeNamespace` (optional): override runtime namespace; empty/missing => `BGCS.Runtime`
- `EntryFiles`: parser entry files
- `allowedHeaders`: strict output whitelist
  - omitted/null: defaults to `EntryFiles`
  - empty array: output nothing
  - non-empty: output only listed files

## 2) BGCS.Cpp2C.Demo

Location: `~/demo/BGCS.Cpp2C.Demo`

Run with default config:

```bash
cd ~/demo/BGCS.Cpp2C.Demo
dotnet run
```

Run with explicit config/output:

```bash
cd ~/demo/BGCS.Cpp2C.Demo
dotnet run -- config.json Output
```

Run with all configuration fields explicitly set (showcase profile):

```bash
cd ~/demo/BGCS.Cpp2C.Demo
dotnet run -- config.all-set.json Output
```

This demo generates a C bridge layout:

- `Output/include/*` for public C headers
- `Output/src/*` for C++ implementation shims
- `config.all-set.json` demonstrates explicit non-empty/non-default values for Cpp2C options
