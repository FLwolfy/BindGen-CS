# Portable native source inputs

[简体中文](native-source-inputs.cn.md) | [Native metadata](native-metadata.md) | [C++ cookbook](cpp-extension-cookbook.md)

BGCS generates source. Your application owns native compilation, linking, export
checks, packaging and deployment. A native source manifest describes the files
and requirements to hand to that build system; it is not a compiler command or a
completed native-build receipt.

## Read the published requirements

After successful generation, `GenerationResult.nativeSources` contains a typed
`NativeSourceManifest` for each target. The same data is published at
`<target>/native/metadata/bridge.manifest.json`. To reopen an existing output, use:

```csharp
var manifests = BindingArtifactManifest.ReadNativeSources(
    outputRoot, libraryId, cancellationToken);
```

This SDK method holds the publication lock, verifies its ownership and complete
artifact hashes, and validates the typed native manifests. It does not verify a
consumer's remapped external files or run a compiler. Ordinary parsed C can have
an empty `sources` collection: its declarations describe an upstream library,
not a newly generated implementation.

The manifest separates:

| Member | Meaning |
| --- | --- |
| `sources`, `headers` | Exact generated artifact descriptors relative to the output root |
| `inputRoots` | Logical root IDs scoped to this target manifest |
| `inputs` | Root-relative input filenames, lengths, SHA-256 digests and roles |
| `includeDirectories` | Ordered search directories, retaining system/framework distinctions |
| `headerReferences` | Original entry identities and their portable include references |
| `defines`, `languageStandard`, `languageRequirements` | Declared source and lowering requirements |
| `requiredExports`, `staticExportTable` | Expected exported symbols and an optional static adapter protocol |

Only an input with role `ShimSource` requests an additional consumer translation
unit. `EntryHeader` and `Dependency` inputs are not a list of files to compile.
Parser builtin headers remain authenticated frontend inputs, separate from the
consumer's SDK requirements.

## Bind logical roots to a frozen input tree

`GenerationResult.nativeInputRoots` supplies session-local absolute directory
bindings. It is marked `JsonIgnore`: physical workspace paths are not embedded in
the portable manifest or ordinary result JSON. Keep these bindings in your own
integration when you need to prepare a frozen source tree. An existing manifest
alone cannot reconstruct their original machine locations.

Reuse `source.includeDirectories` for user header search. BGCS adds the default
project root only when entry headers need it, before parsing and snapshotting.
An ambiguous include or a dependency outside declared roots requires an explicit
root; it is not repaired by guessing another compiler's SDK.

Map every logical root using an ordinal-keyed, caller-supplied dictionary:

- Map `generated` to the verified output root.
- Map user/project roots into your frozen source tree, preserving their relative
  relationships and overlapping subroots. Do not flatten each root into an
  unrelated directory: two search roots may intentionally name the same header.
- Map SDK roots to your selected toolchain's SDK, or an authenticated snapshot of
  that SDK. A `sdk-root` identifies dependencies; it does not automatically add a
  compiler include directory. A different SDK requires regeneration or review.
- Check the mapped `inputs` against their recorded lengths and digests before
  using them. Publication verification covers generated artifacts, not a later
  remapping of external inputs.

This example selects generated and shim translation units without scanning the
output directory. `remappedRoots` is supplied by the consuming application;
its `generated` entry must identify `outputRoot`. Include order is preserved.

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BGCS;
using BGCS.Contracts.Artifacts;

static (string[] Sources,
    (string Directory, bool IsSystem, bool IsFramework)[] Includes)
    ReadNativeInputs(string outputRoot, string libraryId, string targetId,
        IReadOnlyDictionary<string, string> remappedRoots,
        CancellationToken cancellationToken)
{
    NativeSourceManifest manifest = BindingArtifactManifest
        .ReadNativeSources(outputRoot, libraryId, cancellationToken)
        .Single(item => item.targetId == targetId);

    string Resolve(string rootId, string relativePath)
    {
        string root = remappedRoots[rootId];
        if (!Path.IsPathFullyQualified(root))
            throw new ArgumentException("Each remapped root must be absolute.");
        return Path.GetFullPath(relativePath, root);
    }

    string[] generated = manifest.sources
        .Select(file => Resolve("generated", file.relativePath)).ToArray();
    string[] shims = manifest.inputs.Where(file => file.role == "ShimSource")
        .Select(file => Resolve(file.rootId, file.relativePath)).ToArray();
    var includes = manifest.includeDirectories.Select(directory => (
        Directory: Resolve(directory.rootId, directory.relativePath),
        IsSystem: directory.isSystem,
        IsFramework: directory.isFramework)).ToArray();
    return (generated.Concat(shims).ToArray(), includes);
}
```

The consumer translates these ordered requirements into its own compiler and
linker inputs, also honoring defines, language requirements, exports and upstream
libraries. The example does not compile or execute anything.

## Analysis entries and public headers

A `.cpp` file can be an analysis input, but a translation unit is not necessarily
a suitable public include. Generated bridge declarations should include a
public, self-contained header. Keep implementation `.cpp` files in the consumer's
build, or declare a project shim through `cpp.nativeShims[].sourceFiles` with its
separate `publicHeaders`. Including an implementation file from a bridge while
also compiling it can duplicate definitions. BGCS does not rewrite user-owned
source to manufacture a public interface.

## Related 3.0.1 fixes

Dependent template arguments now retain shared type references rather than
claiming ownership of an alias or parameter that already belongs to a declaration.
This fixes parsing patterns such as two `std::pair<iterator, bool>` overloads in a
class template without clearing parents, cloning declarations or weakening AST
ownership checks. It does not make every dependent C++ API lowerable.

Worker-reported parse failures keep a concise diagnostic `message` and preserve bounded stderr
context in optional `BindingDiagnostic.details`. Structured SDK/JSON results can
carry that context; normal diagnostic presentation and `GetDiagnosticSummary()`
remain concise. Retained worker details are limited to a 64 Ki-character tail
plus a truncation marker; absent details are omitted from JSON.

These are implementation changes. Release availability and platform acceptance
remain separate from the source manifest and generation result.
