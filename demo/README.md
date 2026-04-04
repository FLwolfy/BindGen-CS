# BGCS Demo Workspace

This folder contains two standalone demo apps extracted from the repository tests:

- `BGCS.Demo`: C/C++/COM -> C# bindings demo via `BGCS`
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
dotnet run -- config.json Output
```

Run all built-in scenarios:

```bash
cd ~/demo/BGCS.Demo
dotnet run -- --all Output
```

Config notes:

- `EntryFiles`: parser entry files
- `OutputFilterFiles`: strict output whitelist
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

This demo generates a C bridge layout:

- `Output/include/*` for public C headers
- `Output/src/*` for C++ implementation shims