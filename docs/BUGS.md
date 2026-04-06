# Known Bugs

This file tracks confirmed bugs in BGCS that affect generation correctness or developer experience.

## 1. `CustomEnums` comments can generate invalid C# code

- Area: `BGCS` (`EnumGenerationStep`)
- Status: Confirmed
- Severity: High

### Repro

Use `CsCodeGeneratorConfig.CustomEnums` with plain string comments:

- `CustomEnums[i].Comment = "Some text"`
- `CustomEnums[i].Items[j].Comment = "Some item text"`

### Expected

Comments should be emitted as valid C# comments/XML docs (or omitted based on config behavior).

### Actual

Raw text can be written directly into output, producing invalid C# (e.g. bare `Some text` line in enum body/scope).

### Impact

- Generated `Bindings.cs` may not compile.
- Demo output can look broken even when parser/generator otherwise succeeds.

### Suspected cause

`CustomEnums` path writes `csEnum.Comment` / `csEnumItem.Comment` directly without always normalizing through `WriteCsSummary(...)`.

---

## 2. Manual `FunctionTableEntries` without `Index` produce duplicate index `0`

- Area: `BGCS` (`FunctionTableBuilder.Append`)
- Status: Confirmed
- Severity: High

### Repro

Provide `FunctionTableEntries` in config with only `EntryPoint` and no explicit `Index`.

### Expected

Either:

- auto-assign sequential indices (`0..N-1`), or
- fail fast with clear validation error requiring explicit indices.

### Actual

All entries default to `Index = 0`, resulting in repeated:

```csharp
funcTable.Load(0, "..."); // repeated for multiple entry points
```

### Impact

- Function table slot mapping is incorrect.
- Runtime calls can dispatch to wrong function pointers.

### Suggested fix direction

- Validate `FunctionTableEntries` before append, or
- auto-normalize missing/duplicate indices.

---

## 3. `BaseConfig.Url` with `file://` depends on current working directory

- Area: `BGCS` (`ConfigComposer.LoadBaseConfig`)
- Status: Confirmed
- Severity: Medium

### Repro

Use config:

```json
{
  "BaseConfig": { "Url": "file://config.base.json" }
}
```

Run from a different working directory than the config file location.

### Expected

`file://` relative path should resolve relative to the current config file (or be clearly documented + validated).

### Actual

Path is resolved against process working directory. This can load the wrong file or fail unexpectedly.

### Impact

- Different environments can read different base config files.
- Demo behavior becomes inconsistent depending on run location.

