# Binding Generator Quality Issue Tracking

This file tracks the five quality issues raised during review, plus resolution status and verification coverage.

## 1. Callback pointer strategy drift (`DelegatesAsVoidPointer`)
- Status: Resolved
- Problem:
  - Delegate pointer handling was inconsistent across generation paths (`TypeGenerationStep`, `CppTypeConverter`, `DelegateParameterWriter`, function return handling).
- Fix:
  - Introduced and used `GetDelegatePointerType(...)` in config to centralize callback pointer strategy.
  - Updated converter and writer paths to respect `DelegatesAsVoidPointer`.
- Verification:
  - `tests/BGCS.Tests/ParserInterop/DelegatePointerConsistencyTests.cs`

## 2. Constant normalization dead branch
- Status: Resolved
- Problem:
  - `NormalizeConstantValue` used impossible combined `StartsWith` conditions.
- Fix:
  - Replaced impossible `&&` chain with the intended prefix alternatives.
- Verification:
  - `tests/BGCS.Tests/Regression/FormatHelperTests.cs`

## 3. Macro filtering path leak for empty source path
- Status: Resolved
- Problem:
  - Empty macro source paths could pass user-header filtering.
- Fix:
  - `FileSet.Contains("")` now returns `false`.
  - Constant generation explicitly ignores macros with empty source file.
- Verification:
  - `tests/BGCS.Core.Tests/FileSetTests.cs`

## 4. `extern "C"` test coverage gap
- Status: Resolved
- Problem:
  - Existing tests did not cover nested namespace/extern blocks, mixed extern/non-extern declarations, and class-member false positives.
- Fix:
  - Added coverage for namespace-contained extern blocks.
  - Added coverage for mixed extern/non-extern declarations in one header.
  - Added coverage to ensure class members in extern blocks are not treated as public exports.
- Verification:
  - `tests/BGCS.Tests/ParserInterop/ExternCDetectionTests.cs`
  - `tests/BGCS.Generation.Compilation.Tests/GeneratedCodeCompilationMatrixTests.cs`

## 5. Loose assertion pattern in binding tests
- Status: Resolved
- Problem:
  - Callback assertions allowed overly permissive alternatives.
- Fix:
  - Tightened callback signature assertions from OR-based fallback to exact signature checks.
  - Added Roslyn compile + reflection matrix tests for stronger semantics.
- Verification:
  - `tests/BGCS.Tests/Regression/BindingCorrectnessMatrixTests.cs`
  - `tests/BGCS.Tests/Regression/BindingCoverageTests.cs`
  - `tests/BGCS.Generation.Compilation.Tests/GeneratedCodeCompilationMatrixTests.cs`
