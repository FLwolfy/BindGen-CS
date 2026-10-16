# Objective-C declaration facts

`BindingHost.AnalyzeAsync` accepts `SourceLanguage.ObjC`. Its default frontend runs in the isolated Clang worker and returns a detached `NativeModule`. A resolved target, actual include paths and any required Apple SDK remain explicit inputs. A header containing only self-contained declarations can be parsed for a Darwin target without an Apple SDK; that proves parsing only.

Objective-C relationships have dedicated neutral records on `NativeDeclaration`:

- `objectiveCContainer` distinguishes interfaces, protocols and categories, and preserves protocol declarations, generic parameter types, the base interface and a category's target interface.
- `objectiveCMethod` preserves the full selector and class or instance dispatch domain.
- `objectiveCProperty` preserves the actual value type through `NativeDeclaration.type`, compiler-reported property flags and exact getter/setter declarations and selectors. `copy`, `weak`, `strong` and similar flags remain source facts; they do not create managed ownership or ARC rules.

These records contain stable identities and immutable data. They do not contain Clang cursors. Graph verification checks their references, and declaration selection retains required protocol, generic and accessor relationships. Missing attribute information remains unknown.

Objective-C generic parameters and interface inheritance are separate from C++ template and base-class access semantics. Blocks remain their own native type category, rather than ordinary C function pointers. An `instancetype` result retains its compiler-established native pointer graph.

Generation requests currently accept C and C++. Objective-C facts do not enable message-send bindings, block invocation, ARC management, native linking or runtime certification. `AnalyzeAsync` does not emit a C import for an Objective-C selector. Parsing a self-contained fixture for an Apple target does not certify an Apple platform.
